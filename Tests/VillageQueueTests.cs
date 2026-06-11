using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using VillageSimulator.Data;
using VillageSimulator.Models;
using VillageSimulator.Controllers;
using VillageSimulator.Services;
using Xunit;
using Microsoft.AspNetCore.Mvc;

public class VillageQueueTests
{
    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public void QueueingSequentialUpgrades_UsesQueuedLevelAndSerializes()
    {
        using var db = CreateInMemoryContext();

        Village village = new() { Name = "V1", Wood = 10000, Clay = 10000, Iron = 10000 };
        db.Villages.Add(village);

        Building building = new() { VillageId = village.Id, Type = BuildingType.WoodCamp, Level = 1 };
        db.Buildings.Add(building);
        db.SaveChanges();

        ResourceService resourceService = new(db);
        VillageController controller = new(db, resourceService);

        // First upgrade -> level 2
        var result1 = controller.Upgrade(building.Id) as RedirectToActionResult;
        Assert.NotNull(result1);
        var queue = db.BuildQueueItems.Where(q => q.VillageId == village.Id).ToList();
        Assert.Single(queue);
        Assert.Equal(2, queue[0].TargetLevel);

        // Second upgrade queued before first completes -> should target level 3
        var result2 = controller.Upgrade(building.Id) as RedirectToActionResult;
        Assert.NotNull(result2);
        queue = db.BuildQueueItems.Where(q => q.VillageId == village.Id).OrderBy(q => q.StartTime).ToList();
        Assert.Equal(2, queue.Count);
        Assert.Equal(2, queue[0].TargetLevel);
        Assert.Equal(3, queue[1].TargetLevel);

        // Ensure second starts after first finishes
        Assert.True(queue[1].StartTime >= queue[0].FinishTime);
    }
}
