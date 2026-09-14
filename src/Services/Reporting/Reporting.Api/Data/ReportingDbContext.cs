using MassTransit;
using Microsoft.EntityFrameworkCore;
using Reporting.Api.Domain;

namespace Reporting.Api.Data;

public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
    public DbSet<EventSalesProjection> EventSales => Set<EventSalesProjection>();
    public DbSet<TierSalesProjection> TierSales => Set<TierSalesProjection>();

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
