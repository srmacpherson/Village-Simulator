namespace VillageSimulator.Models;

public class BuildQueueItem
{
    public int Id { get; set; }

    public int VillageId { get; set; }

    public BuildingType BuildingType { get; set; }

    public int TargetLevel { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime FinishTime { get; set; }

    // Costs paid when enqueuing the upgrade - used for refunds
    public int WoodCost { get; set; }
    public int ClayCost { get; set; }
    public int IronCost { get; set; }
}
