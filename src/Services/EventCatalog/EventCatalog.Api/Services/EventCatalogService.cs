using EventCatalog.Api.Contracts;
using EventCatalog.Api.Data;
using EventCatalog.Api.Domain;
using EventTicketing.Contracts.Messages;
using EventTicketing.ServiceDefaults.Correlation;
using EventTicketing.ServiceDefaults.Errors;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace EventCatalog.Api.Services;

public sealed class EventCatalogService(
    EventCatalogDbContext db,
    IPublishEndpoint publisher,
    ICorrelationContext correlationContext,
    TimeProvider timeProvider) : IEventCatalogService
{
    public async Task<IReadOnlyList<EventResponse>> GetAllAsync(CancellationToken cancellationToken) =>
        (await db.Events.AsNoTracking()
            .Include(entity => entity.PricingTiers)
            .OrderBy(entity => entity.StartsAtUtc)
            .ToListAsync(cancellationToken))
        .Select(Map)
        .ToList();

    public async Task<EventResponse> GetAsync(Guid eventId, CancellationToken cancellationToken) =>
        Map(await FindAsync(eventId, cancellationToken));

    public async Task<EventResponse> CreateAsync(
        CreateEventRequest request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        EventRequestValidator.Validate(request, now);

        var entity = new EventEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            Venue = request.Venue.Trim(),
            StartsAtUtc = request.StartsAtUtc.ToUniversalTime(),
            TotalCapacity = request.TotalCapacity,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            PricingTiers = MapTiers(request.PricingTiers)
        };

        db.Events.Add(entity);
        var message = ToCreatedMessage(entity, correlationContext.CorrelationId, now);
        await publisher.Publish(message, context => context.CorrelationId = message.CorrelationId,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<EventResponse> UpdateAsync(
        Guid eventId,
        UpdateEventRequest request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        EventRequestValidator.Validate(request, now);
        var entity = await FindAsync(eventId, cancellationToken);

        if (entity.Version != request.Version)
        {
            throw new ResourceConflictException(
                $"Event version {request.Version} is stale; the current version is {entity.Version}.");
        }

        entity.Name = request.Name.Trim();
        entity.Description = request.Description.Trim();
        entity.Venue = request.Venue.Trim();
        entity.StartsAtUtc = request.StartsAtUtc.ToUniversalTime();
        entity.TotalCapacity = request.TotalCapacity;
        entity.Version++;
        entity.UpdatedAtUtc = now;

        ReconcilePricingTiers(entity.PricingTiers, request.PricingTiers);

        var message = ToUpdatedMessage(entity, correlationContext.CorrelationId, now);
        await publisher.Publish(message, context => context.CorrelationId = message.CorrelationId,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task DeleteAsync(Guid eventId, long version, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(eventId, cancellationToken);
        if (entity.Version != version)
        {
            throw new ResourceConflictException(
                $"Event version {version} is stale; the current version is {entity.Version}.");
        }

        var deletedVersion = entity.Version + 1;
        var occurredAt = timeProvider.GetUtcNow();
        var message = new EventDeletedV1(entity.Id, deletedVersion,
            correlationContext.CorrelationId, occurredAt);

        db.Events.Remove(entity);
        await publisher.Publish(message, context => context.CorrelationId = message.CorrelationId,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<EventEntity> FindAsync(Guid eventId, CancellationToken cancellationToken) =>
        await db.Events.Include(entity => entity.PricingTiers)
            .SingleOrDefaultAsync(entity => entity.Id == eventId, cancellationToken)
        ?? throw new ResourceNotFoundException($"Event '{eventId}' was not found.");

    private static List<PricingTierEntity> MapTiers(IEnumerable<PricingTierRequest> tiers) =>
        tiers.Select(tier => new PricingTierEntity
        {
            Id = Guid.NewGuid(),
            Name = tier.Name.Trim(),
            Price = tier.Price,
            Capacity = tier.Capacity
        }).ToList();

    internal static void ReconcilePricingTiers(
        List<PricingTierEntity> existingTiers,
        IEnumerable<PricingTierRequest> requestedTiers)
    {
        var requested = requestedTiers.ToList();
        var requestedNames = requested
            .Select(tier => tier.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        existingTiers.RemoveAll(tier => !requestedNames.Contains(tier.Name));

        foreach (var requestedTier in requested)
        {
            var name = requestedTier.Name.Trim();
            var existing = existingTiers.SingleOrDefault(tier =>
                string.Equals(tier.Name, name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                existingTiers.Add(new PricingTierEntity
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Price = requestedTier.Price,
                    Capacity = requestedTier.Capacity
                });
                continue;
            }

            existing.Name = name;
            existing.Price = requestedTier.Price;
            existing.Capacity = requestedTier.Capacity;
        }
    }

    private static EventResponse Map(EventEntity entity) => new(
        entity.Id,
        entity.Name,
        entity.Description,
        entity.Venue,
        entity.StartsAtUtc,
        entity.TotalCapacity,
        entity.Version,
        entity.PricingTiers.OrderBy(tier => tier.Price)
            .Select(tier => new PricingTierResponse(tier.Id, tier.Name, tier.Price, tier.Capacity))
            .ToList());

    private static EventCreatedV1 ToCreatedMessage(
        EventEntity entity,
        Guid correlationId,
        DateTimeOffset occurredAt) => new(
            entity.Id, entity.Name, entity.Description, entity.Venue, entity.StartsAtUtc,
            entity.TotalCapacity, entity.Version, ToContracts(entity.PricingTiers), correlationId, occurredAt);

    private static EventUpdatedV1 ToUpdatedMessage(
        EventEntity entity,
        Guid correlationId,
        DateTimeOffset occurredAt) => new(
            entity.Id, entity.Name, entity.Description, entity.Venue, entity.StartsAtUtc,
            entity.TotalCapacity, entity.Version, ToContracts(entity.PricingTiers), correlationId, occurredAt);

    private static IReadOnlyList<PricingTierContract> ToContracts(IEnumerable<PricingTierEntity> tiers) =>
        tiers.Select(tier => new PricingTierContract(tier.Id, tier.Name, tier.Price, tier.Capacity)).ToList();
}
