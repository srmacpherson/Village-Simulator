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
    public void CascadeCancel_RemovesDependentUpgradesOnlyForSameBuilding()
    {
        using var db = CreateInMemoryContext();

        var village = new Village { Name = "V1", Wood = 10000, Clay = 10000, Iron = 10000 };
        db.Villages.Add(village);

        var b1 = new Building { Village = village, Type = BuildingType.WoodCamp, Level = 1 };
        var b2 = new Building { Village = village, Type = BuildingType.ClayPit, Level = 1 };
        db.Buildings.AddRange(b1, b2);
        db.SaveChanges();

        // Create queued upgrades: two for b1 (targets 2 and 3) and one for b2 (target 2)
        var q1 = new BuildQueueItem
        {
            VillageId = village.Id,
            BuildingType = b1.Type,
            TargetLevel = 2,
            StartTime = DateTime.UtcNow.AddMinutes(1),
            FinishTime = DateTime.UtcNow.AddMinutes(2),
            WoodCost = 1000,
            ClayCost = 500,
            IronCost = 250
        };

        var q2 = new BuildQueueItem
        {
            VillageId = village.Id,
            BuildingType = b1.Type,
            TargetLevel = 3,
            StartTime = DateTime.UtcNow.AddMinutes(3),
            FinishTime = DateTime.UtcNow.AddMinutes(6),
            WoodCost = 1500,
            ClayCost = 750,
            IronCost = 375
        };

        var q3 = new BuildQueueItem
        {
            VillageId = village.Id,
            BuildingType = b2.Type,
            TargetLevel = 2,
            StartTime = DateTime.UtcNow.AddMinutes(1),
            FinishTime = DateTime.UtcNow.AddMinutes(2),
            WoodCost = 800,
            ClayCost = 400,
            IronCost = 200
        };

        db.BuildQueueItems.AddRange(q1, q2, q3);

        // Deduct resources as if the upgrades had been queued
        village.Wood -= (q1.WoodCost + q2.WoodCost + q3.WoodCost);
        village.Clay -= (q1.ClayCost + q2.ClayCost + q3.ClayCost);
        village.Iron -= (q1.IronCost + q2.IronCost + q3.IronCost);

        db.SaveChanges();

        var resourceService = new ResourceService(db);
        var controller = new VillageController(db, resourceService);

        // Verify initial queued items present
        var queuedBefore = db.BuildQueueItems.Where(q => q.VillageId == village.Id).ToList();
        Assert.Equal(3, queuedBefore.Count);

        // Now cancel the first queued upgrade for b1 (q1). This should also remove q2 (same building, higher target), but not q3.
        var result = controller.CancelUpgrade(q1.Id) as RedirectToActionResult;
        Assert.NotNull(result);

        var queuedAfter = db.BuildQueueItems.Where(q => q.VillageId == village.Id).ToList();
        // only q3 should remain
        Assert.Single(queuedAfter);
        Assert.Equal(b2.Type, queuedAfter[0].BuildingType);

        // Resources should have been refunded for q1 and q2 but not q3
        var finalVillage = db.Villages.Find(village.Id)!;
        int expectedWood = 10000 - q3.WoodCost; // q1 and q2 refunded
        int expectedClay = 10000 - q3.ClayCost;
        int expectedIron = 10000 - q3.IronCost;

        Assert.Equal(expectedWood, finalVillage.Wood);
        Assert.Equal(expectedClay, finalVillage.Clay);
        Assert.Equal(expectedIron, finalVillage.Iron);
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
