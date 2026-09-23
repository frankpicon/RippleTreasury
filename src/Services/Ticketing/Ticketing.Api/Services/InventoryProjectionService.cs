using EventTicketing.Contracts.Messages;
using Microsoft.EntityFrameworkCore;
using Ticketing.Api.Data;
using Ticketing.Api.Domain;

namespace Ticketing.Api.Services;

public sealed class InventoryProjectionService(
    TicketingDbContext db,
    ILogger<InventoryProjectionService> logger)
{
    public Task ApplyAsync(EventCreatedV1 message, CancellationToken cancellationToken) =>
        UpsertAsync(message.EventId, message.Name, message.StartsAtUtc, message.TotalCapacity,
            message.CatalogVersion, message.PricingTiers, cancellationToken);

    public Task ApplyAsync(EventUpdatedV1 message, CancellationToken cancellationToken) =>
        UpsertAsync(message.EventId, message.Name, message.StartsAtUtc, message.TotalCapacity,
            message.CatalogVersion, message.PricingTiers, cancellationToken);

    public async Task DeleteAsync(EventDeletedV1 message, CancellationToken cancellationToken)
    {
        await db.LockEventAsync(message.EventId, cancellationToken);
        var entity = await db.Events.Include(item => item.PricingTiers)
            .SingleOrDefaultAsync(item => item.Id == message.EventId, cancellationToken);
        if (entity is not null && entity.CatalogVersion >= message.CatalogVersion)
        {
            return;
        }

        if (entity is null)
        {
            entity = new InventoryEvent { Id = message.EventId, Name = "Deleted event" };
            db.Events.Add(entity);
        }

        entity.CatalogVersion = message.CatalogVersion;
        entity.IsActive = false;
        foreach (var tier in entity.PricingTiers)
        {
            tier.IsActive = false;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertAsync(
        Guid eventId,
        string name,
        DateTimeOffset startsAtUtc,
        int requestedTotalCapacity,
        long catalogVersion,
        IReadOnlyList<PricingTierContract> incomingTiers,
        CancellationToken cancellationToken)
    {
        await db.LockEventAsync(eventId, cancellationToken);
        var entity = await db.Events.Include(item => item.PricingTiers)
            .SingleOrDefaultAsync(item => item.Id == eventId, cancellationToken);

        if (entity is not null && entity.CatalogVersion >= catalogVersion)
        {
            return;
        }

        if (entity is null)
        {
            entity = new InventoryEvent
            {
                Id = eventId,
                Name = name,
                StartsAtUtc = startsAtUtc,
                TotalCapacity = requestedTotalCapacity,
                CatalogVersion = catalogVersion,
                PricingTiers = incomingTiers.Select(ToEntity).ToList()
            };
            db.Events.Add(entity);
        }
        else
        {
            entity.Name = name;
            entity.StartsAtUtc = startsAtUtc;
            entity.CatalogVersion = catalogVersion;
            entity.IsActive = true;

            var incomingById = incomingTiers.ToDictionary(tier => tier.TierId);
            foreach (var existing in entity.PricingTiers)
            {
                if (!incomingById.TryGetValue(existing.Id, out var incoming))
                {
                    existing.IsActive = false;
                    continue;
                }

                existing.Name = incoming.Name;
                existing.Price = incoming.Price;
                existing.IsActive = true;
                existing.Capacity = Math.Max(incoming.Capacity, existing.TicketsSold);
                if (incoming.Capacity < existing.TicketsSold)
                {
                    logger.LogWarning(
                        "Event {EventId} attempted to reduce tier {TierId} below {TicketsSold} sold tickets; capacity was preserved",
                        eventId, existing.Id, existing.TicketsSold);
                }
            }

            var existingIds = entity.PricingTiers.Select(tier => tier.Id).ToHashSet();
            foreach (var incoming in incomingTiers.Where(tier => !existingIds.Contains(tier.TierId)))
            {
                var added = ToEntity(incoming);
                entity.PricingTiers.Add(added);
                // IDs come from Catalog. Explicitly insert rather than letting EF infer an existing row.
                db.PricingTiers.Add(added);
            }
            // Retired tiers retain their sales, but do not add seats to the event.
            entity.TotalCapacity = Math.Max(requestedTotalCapacity,
                entity.PricingTiers.Sum(tier => tier.TicketsSold));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static InventoryTier ToEntity(PricingTierContract tier) => new()
    {
        Id = tier.TierId,
        Name = tier.Name,
        Price = tier.Price,
        Capacity = tier.Capacity
    };
}
