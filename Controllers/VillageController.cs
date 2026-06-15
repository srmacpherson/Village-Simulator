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

            // Find dependent queued upgrades for the same building (higher target levels)
            var dependent = _db.BuildQueueItems
                .Where(q => q.VillageId == item.VillageId && q.BuildingType == item.BuildingType && q.TargetLevel > item.TargetLevel)
                .ToList();

            // Sum refunds including the primary item
            int refundWood = item.WoodCost + dependent.Sum(d => d.WoodCost);
            int refundClay = item.ClayCost + dependent.Sum(d => d.ClayCost);
            int refundIron = item.IronCost + dependent.Sum(d => d.IronCost);

            // Apply refunds
            village.Wood += refundWood;
            village.Clay += refundClay;
            village.Iron += refundIron;

            // Remove primary and dependent items
            _db.BuildQueueItems.RemoveRange(dependent);
            _db.BuildQueueItems.Remove(item);
            _db.SaveChanges();

            // Recalculate start/finish times for remaining queue items in this village so timers shift
            var remaining = _db.BuildQueueItems
                .Where(q => q.VillageId == item.VillageId)
                .OrderBy(q => q.StartTime)
                .ToList();

            var nextStart = DateTime.UtcNow;
            foreach (var remainingItem in remaining)
            {
                // duration uses target level as minutes (keeps previous behavior)
                remainingItem.StartTime = nextStart;
                remainingItem.FinishTime = remainingItem.StartTime.AddMinutes(remainingItem.TargetLevel * 1);
                nextStart = remainingItem.FinishTime;
            }

            if (remaining.Count > 0)
            {
                _db.SaveChanges();
            }

            TempData?["UpgradeSuccess"] = $"Upgrade cancelled and resources refunded. " +
               $"{dependent.Count + 1} upgrade(s) cancelled";

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
                    FinishTime = q.FinishTime.ToString("o"),
                    AffectsOthers = _db.BuildQueueItems
                        .Any(other => other.VillageId == q.VillageId && other.BuildingType == q.BuildingType && other.TargetLevel > q.TargetLevel)
                })
                .ToList();

            var msg = TempData != null
                ? TempData["UpgradeError"] as string ?? TempData["UpgradeSuccess"] as string
                : null;

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
                BuildQueue = _db.BuildQueueItems
                    .Where(q => q.VillageId == village.Id)
                    .OrderBy(q => q.StartTime)
                    .ThenBy(q => q.Id)
                    .ToList(),
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

            // Load village resources (include buildings so capacities reflect current levels)
            var village = _db.Villages
                .Include(v => v.Buildings)
                .FirstOrDefault(v => v.Id == building.VillageId);
            if (village == null)
                return NotFound();

            _resourceService.UpdateResources(village);

            // Determine effective current level considering queued upgrades for this building
            var highestQueuedTargetForBuilding = _db.BuildQueueItems
                .Where(q => q.VillageId == village.Id && q.BuildingType == building.Type)
                .Select(q => (int?)q.TargetLevel)
                .Max() ?? 0;

            int effectiveCurrentLevel = Math.Max(building.Level, highestQueuedTargetForBuilding);

            // Calculate cost for next level based on effective level
            var cost = GetUpgradeCost(effectiveCurrentLevel);

            // Check resources
            if (village.Wood < cost.wood || village.Clay < cost.clay || village.Iron < cost.iron)
            {
                // Not enough resources
                if (TempData != null)
                {
                    TempData["UpgradeError"] = "Not enough resources to upgrade.";
                }
                return RedirectToAction(nameof(Index));
            }

            // Deduct resources
            village.Wood -= cost.wood;
            village.Clay -= cost.clay;
            village.Iron -= cost.iron;

            // Determine start time: after last queued finish in the village (no simultaneous builds)
            var lastFinishAcrossVillage = _db.BuildQueueItems
                .Where(q => q.VillageId == village.Id)
                .Select(q => (DateTime?)q.FinishTime)
                .Max();

            var start = DateTime.UtcNow;
            if (lastFinishAcrossVillage.HasValue && lastFinishAcrossVillage.Value > start)
            {
                start = lastFinishAcrossVillage.Value;
            }

            // Target level is next level after effective current level
            int targetLevel = effectiveCurrentLevel + 1;

            // Finish time uses the target level as duration in minutes (keeps previous behavior but uses correct level)
            var finish = start.AddMinutes(targetLevel * 1);

            var queueItem = new BuildQueueItem
            {
                VillageId = village.Id,
                BuildingType = building.Type,
                TargetLevel = targetLevel,
                StartTime = start,
                FinishTime = finish,
                WoodCost = cost.wood,
                ClayCost = cost.clay,
                IronCost = cost.iron
            };
            _db.BuildQueueItems.Add(queueItem);
            _db.SaveChanges();

            if (TempData != null)
            {
                TempData["UpgradeSuccess"] = "Upgrade started.";
            }

            return RedirectToAction(nameof(Index));
        }

        private (int wood, int clay, int iron) GetUpgradeCost(Building building)
        {
            return GetUpgradeCost(building.Level);
        }

        private (int wood, int clay, int iron) GetUpgradeCost(int currentLevel)
        {
            // Simple cost formula: base cost * (current level + 1)
            int baseWood = 200;
            int baseClay = 150;
            int baseIron = 100;

            int multiplier = currentLevel + 1;

            return (baseWood * multiplier, baseClay * multiplier, baseIron * multiplier);
        }
    }
}
