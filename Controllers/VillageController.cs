using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VillageSimulator.Data;
using VillageSimulator.Services;
using VillageSimulator.ViewModels;
using VillageSimulator.Models;

namespace VillageSimulator.Controllers
{
    public class VillageController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly ResourceService _resourceService;

        public VillageController(
            ApplicationDbContext db,
            ResourceService resourceService)
        {
            _db = db;
            _resourceService = resourceService;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CancelUpgrade(int id)
        {
            var item = _db.BuildQueueItems.Find(id);
            if (item == null)
                return NotFound();

            var village = _db.Villages.Find(item.VillageId);
            if (village == null)
                return NotFound();

            // Refund resources
            village.Wood += item.WoodCost;
            village.Clay += item.ClayCost;
            village.Iron += item.IronCost;

            _db.BuildQueueItems.Remove(item);
            _db.SaveChanges();

            TempData["UpgradeSuccess"] = "Upgrade cancelled and resources refunded.";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult Queue()
        {
            var village = _db.Villages
                .Include(v => v.Buildings)
                .FirstOrDefault();

            if (village == null)
                return Json(new { queue = new object[] { }, msg = "", wood = 0, clay = 0, iron = 0 });

            // Update resources and process completed items
            _resourceService.UpdateResources(village);
            _db.SaveChanges();

            var queue = _db.BuildQueueItems
                .Where(q => q.VillageId == village.Id)
                .Select(q => new
                {
                    q.Id,
                    BuildingType = q.BuildingType.ToString(),
                    q.TargetLevel,
                    StartTime = q.StartTime.ToString("o"),
                    FinishTime = q.FinishTime.ToString("o")
                })
                .ToList();

            var msg = TempData["UpgradeError"] as string ?? TempData["UpgradeSuccess"] as string;

            return Json(new { queue, msg, wood = village.Wood, clay = village.Clay, iron = village.Iron });
        }

        public IActionResult Index()
        {
            var village = _db.Villages
                .Include(v => v.Buildings)
                .FirstOrDefault()
                ?? throw new Exception("Village Not Found");

            _resourceService.UpdateResources(village);

            _db.SaveChanges();

            var model = new VillageViewModel
            {
                Name = village.Name,
                Wood = village.Wood.ToString(),
                Clay = village.Clay.ToString(),
                Iron = village.Iron.ToString(),
                WoodCapacity = village.WoodCapacity.ToString(),
                ClayCapacity = village.ClayCapacity.ToString(),
                IronCapacity = village.IronCapacity.ToString(),
                Buildings = village.Buildings,
                BuildQueue = _db.BuildQueueItems.Where(q => q.VillageId == village.Id).ToList(),
                UpgradeMessage = TempData["UpgradeError"] as string ?? TempData["UpgradeSuccess"] as string,
                HQLevel = village.HQLevel.ToString(),
                WallLevel = village.WallLevel.ToString()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Upgrade(int id)
        {
            var building = _db.Buildings.Find(id);
            if (building == null)
            {
                return NotFound();
            }

            // Calculate cost for next level
            var cost = GetUpgradeCost(building);

            // Load village resources
            var village = _db.Villages.Find(building.VillageId);
            if (village == null)
                return NotFound();

            _resourceService.UpdateResources(village);

            // Check resources
            if (village.Wood < cost.wood || village.Clay < cost.clay || village.Iron < cost.iron)
            {
                // Not enough resources
                TempData["UpgradeError"] = "Not enough resources to upgrade.";
                return RedirectToAction(nameof(Index));
            }

            // Deduct resources
            village.Wood -= cost.wood;
            village.Clay -= cost.clay;
            village.Iron -= cost.iron;

            // Simulate build time
            var start = DateTime.UtcNow;
            var finish = start.AddMinutes((building.Level + 1) * 1);
            var queueItem = new BuildQueueItem
            {
                VillageId = village.Id,
                BuildingType = building.Type,
                TargetLevel = building.Level + 1,
                StartTime = start,
                FinishTime = finish,
                WoodCost = cost.wood,
                ClayCost = cost.clay,
                IronCost = cost.iron
            };
            _db.BuildQueueItems.Add(queueItem);
            _db.SaveChanges();

            TempData["UpgradeSuccess"] = "Upgrade started.";

            return RedirectToAction(nameof(Index));
        }

        private (int wood, int clay, int iron) GetUpgradeCost(Building building)
        {
            // Simple cost formula: base cost * (current level + 1)
            int baseWood = 200;
            int baseClay = 150;
            int baseIron = 100;

            int multiplier = building.Level + 1;

            return (baseWood * multiplier, baseClay * multiplier, baseIron * multiplier);
        }
    }
}
