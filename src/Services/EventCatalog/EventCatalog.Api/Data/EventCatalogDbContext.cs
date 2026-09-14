using EventCatalog.Api.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace EventCatalog.Api.Data;

public sealed class EventCatalogDbContext(DbContextOptions<EventCatalogDbContext> options)
    : DbContext(options)
{
    public DbSet<EventEntity> Events => Set<EventEntity>();
    public DbSet<PricingTierEntity> PricingTiers => Set<PricingTierEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventEntity>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2_000).IsRequired();
            entity.Property(x => x.Venue).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => x.StartsAtUtc);
            entity.HasMany(x => x.PricingTiers)
                .WithOne(x => x.Event)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PricingTierEntity>(entity =>
        {
            entity.ToTable("pricing_tiers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Price).HasPrecision(12, 2);
            entity.HasIndex(x => new { x.EventId, x.Name }).IsUnique();
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
