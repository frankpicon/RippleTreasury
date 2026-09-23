using MassTransit;
using Microsoft.EntityFrameworkCore;
using Reporting.Api.Domain;

namespace Reporting.Api.Data;

public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
    public DbSet<EventSalesProjection> EventSales => Set<EventSalesProjection>();
    public DbSet<TierSalesProjection> TierSales => Set<TierSalesProjection>();

    // Catalog and purchase messages use different queues and may be consumed concurrently.
    // Serialize every projection mutation for one event while allowing different events
    // to continue in parallel. The EF consumer outbox supplies the surrounding transaction.
    public Task LockEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Reporting projection writes require a transaction.");

        return Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({eventId.ToString()}, 0))",
            cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventSalesProjection>(entity =>
        {
            entity.ToTable("event_sales");
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.GrossRevenue).HasPrecision(14, 2);
            entity.HasMany(x => x.PricingTiers)
                .WithOne(x => x.Event)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TierSalesProjection>(entity =>
        {
            entity.ToTable("tier_sales");
            entity.HasKey(x => x.PricingTierId);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.GrossRevenue).HasPrecision(14, 2);
            entity.HasIndex(x => x.EventId);
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
