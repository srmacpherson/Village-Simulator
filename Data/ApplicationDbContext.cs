using Microsoft.EntityFrameworkCore;
using VillageSimulator.Models;

namespace VillageSimulator.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Village> Villages { get; set; } = null!;

    public DbSet<Building> Buildings { get; set; } = null!;

    public DbSet<BuildQueueItem> BuildQueueItems { get; set; } = null!;
}
