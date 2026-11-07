using Microsoft.EntityFrameworkCore;
using Vroo.Models;

namespace Vroo.Data;

public class VrooDbContext(DbContextOptions<VrooDbContext> options) : DbContext(options)
{
    public DbSet<WatchedModel> WatchedModels => Set<WatchedModel>();
    public DbSet<Car> Cars => Set<Car>();
    public DbSet<CarPrice> CarPrices => Set<CarPrice>();
    public DbSet<ScrapeRun> ScrapeRuns => Set<ScrapeRun>();
    public DbSet<DailyStat> DailyStats => Set<DailyStat>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<WatchedModel>(e =>
        {
            e.Property(x => x.Make).HasMaxLength(100);
            e.Property(x => x.Model).HasMaxLength(100);
            e.HasIndex(x => new { x.Make, x.Model }).IsUnique();
        });

        b.Entity<Car>(e =>
        {
            e.HasIndex(x => x.Url).IsUnique();
            e.HasIndex(x => new { x.WatchedModelId, x.RegistrationNumber });
            e.HasIndex(x => new { x.WatchedModelId, x.LastSeenAt });
            e.HasOne(x => x.WatchedModel).WithMany(x => x.Cars).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CarPrice>(e =>
        {
            e.HasIndex(x => new { x.CarId, x.ObservedAt });
            e.HasOne(x => x.Car).WithMany(x => x.Prices).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ScrapeRun>(e =>
        {
            e.Property(x => x.Trigger).HasMaxLength(20);
            e.HasIndex(x => new { x.WatchedModelId, x.StartedAt });
            e.HasOne(x => x.WatchedModel).WithMany(x => x.Runs).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DailyStat>(e =>
        {
            e.HasKey(x => new { x.WatchedModelId, x.Date });
            e.HasOne(x => x.WatchedModel).WithMany(x => x.DailyStats).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
