using System;
using System.Linq;
using VillageSimulator.Models;
using VillageSimulator.Data;
using Microsoft.EntityFrameworkCore;

namespace VillageSimulator.Services;

public class ResourceService
{
    private readonly ApplicationDbContext _db;

    public ResourceService(ApplicationDbContext db)
    {
        _db = db;
    }

    public void UpdateResources(Village village)
    {
        double timeSinceLastUpdated = (DateTime.UtcNow - village.LastUpdated).TotalHours;

        if (timeSinceLastUpdated > 0)
        {
            // Calculate produced resources (per-hour rates) and convert to whole units
            int producedWood = (int)Math.Floor(GetWoodProduction(village) * timeSinceLastUpdated);
            int producedClay = (int)Math.Floor(GetClayProduction(village) * timeSinceLastUpdated);
            int producedIron = (int)Math.Floor(GetIronProduction(village) * timeSinceLastUpdated);

            // Add and clamp to capacity
            village.Wood = Math.Min(village.Wood + producedWood, village.WoodCapacity);
            village.Clay = Math.Min(village.Clay + producedClay, village.ClayCapacity);
            village.Iron = Math.Min(village.Iron + producedIron, village.IronCapacity);
        }

        // Process completed build queue items for this village
        var currentTime = DateTime.UtcNow;
        var completedQueueItems = _db.BuildQueueItems
            .Where(q => q.VillageId == village.Id && q.FinishTime <= currentTime)
            .ToList();

        if (completedQueueItems.Count != 0)
        {
            // Ensure buildings are loaded
            _db.Entry(village).Collection(v => v.Buildings).Load();

            foreach (var item in completedQueueItems)
            {
                Building? building = village.Buildings.FirstOrDefault(b => b.Type == item.BuildingType);

                if (building == null)
                {
                    building = new Building { VillageId = village.Id, Type = item.BuildingType, Level = item.TargetLevel };
                    village.Buildings.Add(building);
                    _db.Buildings.Add(building);
                }
                else
                {
                    building.Level = item.TargetLevel;
                }

                _db.BuildQueueItems.Remove(item);
            }
        }

        village.LastUpdated = DateTime.UtcNow;
    }

    private double GetWoodProduction(Village village) => 1.0;
    private double GetClayProduction(Village village) => 0.8;
    private double GetIronProduction(Village village) => 0.5;
}
