namespace VillageSimulator.Models;

public class Building
{
    public int Id { get; set; }

    public int VillageId { get; set; }

    public BuildingType Type { get; set; }

    public int Level { get; set; }

    public Village? Village { get; set; }
}
