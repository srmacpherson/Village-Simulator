using Microsoft.EntityFrameworkCore;
using VillageSimulator.Data;
using VillageSimulator.Models;
using VillageSimulator.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Register DbContext (in-memory for stubs) and ResourceService
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseInMemoryDatabase("VillageDb"));
builder.Services.AddScoped<ResourceService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Only redirect HTTP to HTTPS in non-development environments to avoid
// redirecting to an HTTPS endpoint that may not be configured in dev.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Village}/{action=Index}/{id?}");

// Seed a sample village for development/testing
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (!db.Villages.Any())
    {
        var village = new Village
        {
            Name = "My Village",
            Wood = 1000,
            Clay = 800,
            Iron = 500,
            HQLevel = 5,
            WallLevel = 3,
            Buildings =
            [
                new() { Type = BuildingType.WoodCamp, Level = 1 },
                new() { Type = BuildingType.ClayPit, Level = 1 },
                new() { Type = BuildingType.IronMine, Level = 1 },
                new() { Type = BuildingType.HQ, Level = 5 },
                new() { Type = BuildingType.Warehouse, Level = 1 },
                new() { Type = BuildingType.Wall, Level = 3 }
            ]
        };

        db.Villages.Add(village);
        db.SaveChanges();
    }
}

app.Run();