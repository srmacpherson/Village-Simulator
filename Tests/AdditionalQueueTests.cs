using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using VillageSimulator.Data;
using VillageSimulator.Models;
using VillageSimulator.Controllers;
using VillageSimulator.Services;
using Xunit;
using Microsoft.AspNetCore.Mvc;

public class AdditionalQueueTests
{
    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public void CancelUpgrade_RefundsResourcesAndRemovesFromQueue()
    {
        using var db = CreateInMemoryContext();

        var village = new Village { Name = "V1", Wood = 10000, Clay = 10000, Iron = 10000 };
        db.Villages.Add(village);

        var building = new Building { Village = village, Type = BuildingType.WoodCamp, Level = 20 };
        db.Buildings.Add(building);
        db.SaveChanges();

        var resourceService = new ResourceService(db);
        var controller = new VillageController(db, resourceService);

        // Manually create a queued item and deduct resources to simulate queuing
        var queued = new BuildQueueItem
        {
            VillageId = village.Id,
            BuildingType = BuildingType.WoodCamp,
            TargetLevel = building.Level + 1,
            StartTime = DateTime.UtcNow,
            FinishTime = DateTime.UtcNow.AddMinutes(1),
            WoodCost = 1000,
            ClayCost = 1000,
            IronCost = 1000
        };
        db.BuildQueueItems.Add(queued);

        // Deduct resources as queue would have
        village.Wood -= queued.WoodCost;
        village.Clay -= queued.ClayCost;
        village.Iron -= queued.IronCost;

        db.SaveChanges();

        var allQueue = db.BuildQueueItems.ToList();
        Assert.Single(allQueue);

        var item = allQueue[0];

        // Resources were deducted when queuing
        var afterDeductWood = db.Villages.Find(village.Id)!.Wood;
        Assert.Equal(9000, afterDeductWood);

        // Cancel upgrade -> should refund and remove
        var cancelResult = controller.CancelUpgrade(item.Id) as RedirectToActionResult;
        Assert.NotNull(cancelResult);

        var queueAfter = db.BuildQueueItems.ToList();
        Assert.Empty(queueAfter);

        var finalVillage = db.Villages.Find(village.Id)!;
        Assert.Equal(10000, finalVillage.Wood);
        Assert.Equal(10000, finalVillage.Clay);
        Assert.Equal(10000, finalVillage.Iron);
    }

    [Fact]
    public void InsufficientResources_PreventsQueueing()
    {
        using var db = CreateInMemoryContext();

        var village = new Village { Name = "V1", Wood = 0, Clay = 0, Iron = 0 };
        db.Villages.Add(village);

        var building = new Building { VillageId = village.Id, Type = BuildingType.WoodCamp, Level = 1 };
        db.Buildings.Add(building);
        db.SaveChanges();

        var resourceService = new ResourceService(db);
        var controller = new VillageController(db, resourceService);

        var result = controller.Upgrade(building.Id) as RedirectToActionResult;
        Assert.NotNull(result);

        var queue = db.BuildQueueItems.Where(q => q.VillageId == village.Id).ToList();
        Assert.Empty(queue);

        var reloaded = db.Buildings.Find(building.Id)!;
        Assert.Equal(1, reloaded.Level);
    }

    [Fact]
    public void QueueingDifferentBuildings_SerializesAcrossVillage()
    {
        using var db = CreateInMemoryContext();

        var village = new Village { Name = "V1", Wood = 10000, Clay = 10000, Iron = 10000 };
        db.Villages.Add(village);

        var b1 = new Building { Village = village, Type = BuildingType.WoodCamp, Level = 1 };
        var b2 = new Building { Village = village, Type = BuildingType.ClayPit, Level = 1 };
        db.Buildings.AddRange(b1, b2);
        db.SaveChanges();

        var resourceService = new ResourceService(db);
        var controller = new VillageController(db, resourceService);

        var persistedB1 = db.Buildings.First(b => b.Type == BuildingType.WoodCamp && b.VillageId == village.Id);
        var persistedB2 = db.Buildings.First(b => b.Type == BuildingType.ClayPit && b.VillageId == village.Id);

        var r1 = controller.Upgrade(persistedB1.Id) as RedirectToActionResult;
        Assert.NotNull(r1);

        var r2 = controller.Upgrade(persistedB2.Id) as RedirectToActionResult;
        Assert.NotNull(r2);

        var queue = db.BuildQueueItems.Where(q => q.VillageId == village.Id).OrderBy(q => q.StartTime).ToList();
        Assert.Equal(2, queue.Count);
        Assert.True(queue[1].StartTime >= queue[0].FinishTime);
    }
}
