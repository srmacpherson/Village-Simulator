using Microsoft.EntityFrameworkCore;
using VillageSimulator.Models;

namespace VillageSimulator.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Village> Villages { get; set; } = null!;

        public DbSet<Building> Buildings { get; set; } = null!;

        public DbSet<BuildQueueItem> BuildQueueItems { get; set; } = null!;
    }
}
