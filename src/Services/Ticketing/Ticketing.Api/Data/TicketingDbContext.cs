using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ticketing.Api.Domain;

namespace Ticketing.Api.Data;

public sealed class TicketingDbContext(DbContextOptions<TicketingDbContext> options) : DbContext(options)
{
    public DbSet<InventoryEvent> Events => Set<InventoryEvent>();
    public DbSet<InventoryTier> PricingTiers => Set<InventoryTier>();
    public DbSet<TicketPurchase> TicketPurchases => Set<TicketPurchase>();

    // Shared by HTTP purchases and catalog consumers, including events not created yet.
    // The transaction-scoped database lock works across service replicas.
    public Task LockEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Inventory writes require a transaction.");
        return Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({eventId.ToString()}, 0))",
            cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryEvent>(entity =>
        {
            entity.ToTable("inventory_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasMany(x => x.PricingTiers)
                .WithOne(x => x.Event)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.StartsAtUtc);
        });

        modelBuilder.Entity<InventoryTier>(entity =>
        {
            entity.ToTable("inventory_tiers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Price).HasPrecision(12, 2);
            entity.HasIndex(x => new { x.EventId, x.IsActive });
        });

        modelBuilder.Entity<TicketPurchase>(entity =>
        {
            entity.ToTable("ticket_purchases");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CustomerEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.BuyerSubject).HasMaxLength(200).IsRequired();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PricingTierName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.UnitPrice).HasPrecision(12, 2);
            entity.Property(x => x.TotalPrice).HasPrecision(14, 2);
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.HasIndex(x => new { x.EventId, x.PurchasedAtUtc });
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
