using System;
using System.Collections.Generic;
using System.Linq;

namespace VillageSimulator.Models;

public class Village
{
	public int Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public int HQLevel { get; set; }
	public int WallLevel { get; set; }
	public int Wood { get; set; }
	public int Clay { get; set; }
	public int Iron { get; set; }
	public int WarehouseLevel { get; set; }

    // Navigation to building levels which determine capacities
    public List<Building> Buildings { get; set; } = new List<Building>();

	// Last time resources were updated
	public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

	// Fractional remainder of produced resources carried between updates
	// These allow production to accumulate when UpdateResources is called frequently
	public double WoodFraction { get; set; }
	public double ClayFraction { get; set; }
	public double IronFraction { get; set; }

	// Capacities derived from the warehouse level
	private const int BaseStorage = 1000;
	private const int PerLevelStorage = 500;
	public int WoodCapacity => BaseStorage + (WarehouseLevel * PerLevelStorage);
	public int ClayCapacity => BaseStorage + (WarehouseLevel * PerLevelStorage);
	public int IronCapacity => BaseStorage + (WarehouseLevel * PerLevelStorage);
}
