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
            // Calculate produced resources (per-hour rates)
            double producedWood = GetWoodProduction(village) * timeSinceLastUpdated;
            double producedClay = GetClayProduction(village) * timeSinceLastUpdated;
            double producedIron = GetIronProduction(village) * timeSinceLastUpdated;

            // Add fractional parts to village's fractional accumulators
            village.WoodFraction += producedWood;
            village.ClayFraction += producedClay;
            village.IronFraction += producedIron;

            // Convert whole units from the fractional accumulators
            int addWood = (int)Math.Floor(village.WoodFraction);
            int addClay = (int)Math.Floor(village.ClayFraction);
            int addIron = (int)Math.Floor(village.IronFraction);

            // Subtract used whole units from the fractions
            village.WoodFraction -= addWood;
            village.ClayFraction -= addClay;
            village.IronFraction -= addIron;

            // Add and clamp to capacity
            village.Wood = Math.Min(village.Wood + addWood, village.WoodCapacity);
            village.Clay = Math.Min(village.Clay + addClay, village.ClayCapacity);
            village.Iron = Math.Min(village.Iron + addIron, village.IronCapacity);
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

    private double GetWoodProduction(Village village)
    {
        int woodBaseProduction = 1000;

        int buildingLevel = village.Buildings.FirstOrDefault(b => b.Type == BuildingType.WoodCamp)?.Level ?? 0;

        double multiplier = buildingLevel * 0.8;

        return woodBaseProduction * multiplier;
    }
    private double GetClayProduction(Village village)
    {
        int clayBaseProduction = 800;

        int buildingLevel = village.Buildings.FirstOrDefault(b => b.Type == BuildingType.ClayPit)?.Level ?? 0;

        double multiplier = buildingLevel * 0.7;

        return clayBaseProduction * multiplier;
    }
    private double GetIronProduction(Village village)
    {
        int ironBaseProduction = 500;

        int buildingLevel = village.Buildings.FirstOrDefault(b => b.Type == BuildingType.IronMine)?.Level ?? 0;

        double multiplier = buildingLevel * 0.6;

        return ironBaseProduction * multiplier;
    }
}
