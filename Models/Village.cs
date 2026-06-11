using System;
using System.Collections.Generic;
using System.Linq;

namespace VillageSimulator.Models;

public class Village
{
	private const int BaseStorage = 1000;
	private const int PerLevelStorage = 500;

	public int Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public int HQLevel { get; set; }
	public int WallLevel { get; set; }
	public int Wood { get; set; }
	public int Clay { get; set; }
	public int Iron { get; set; }

	// Navigation to building levels which determine capacities
	public List<Building> Buildings { get; set; } = new List<Building>();

	// Last time resources were updated
	public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

	// Capacities derived from the associated building levels
	public int WoodCapacity => BaseStorage + (Buildings.FirstOrDefault(b => b.Type == BuildingType.WoodCamp)?.Level ?? 0) * PerLevelStorage;
	public int ClayCapacity => BaseStorage + (Buildings.FirstOrDefault(b => b.Type == BuildingType.ClayPit)?.Level ?? 0) * PerLevelStorage;
	public int IronCapacity => BaseStorage + (Buildings.FirstOrDefault(b => b.Type == BuildingType.IronMine)?.Level ?? 0) * PerLevelStorage;
}
