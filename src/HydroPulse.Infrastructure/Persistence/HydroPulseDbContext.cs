using HydroPulse.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace HydroPulse.Infrastructure.Persistence;

public class HydroPulseDbContext : DbContext
{
    public DbSet<TelemetryRecordEntity> TelemetryRecords => Set<TelemetryRecordEntity>();

    public HydroPulseDbContext(DbContextOptions<HydroPulseDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TelemetryRecordEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.HasIndex(e => new { e.StackId, e.Timestamp });
        });
    }
}