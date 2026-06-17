using System.Collections.Generic;
using VillageSimulator.Models;

namespace VillageSimulator.ViewModels;

public class VillageViewModel
{
    public required string Name { get; init; }
    public required string Wood { get; init; }
    public required string Clay { get; init; }
    public required string Iron { get; init; }
    public required string WoodCapacity { get; init; }
    public required string ClayCapacity { get; init; }
    public required string IronCapacity { get; init; }
    public required string HQLevel { get; init; }
    public required string WallLevel { get; init; }
    public required string WarehouseLevel { get; init; }
    public string? UpgradeMessage { get; init; }
    public List<Building> Buildings { get; init; } = [];
    public List<BuildQueueItem> BuildQueue { get; init; } = [];
}
