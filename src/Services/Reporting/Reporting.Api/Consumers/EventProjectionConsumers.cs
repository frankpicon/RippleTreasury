using EventTicketing.Contracts.Messages;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Reporting.Api.Data;
using Reporting.Api.Domain;

namespace Reporting.Api.Consumers;

public sealed class EventCreatedConsumer(
    ReportingDbContext db,
    TimeProvider timeProvider,
    ILogger<EventCreatedConsumer> logger) : IConsumer<EventCreatedV1>
{
    public async Task Consume(ConsumeContext<EventCreatedV1> context)
    {
        await SalesProjectionWriter.UpsertEventAsync(db, context.Message.EventId, context.Message.Name,
            context.Message.StartsAtUtc, context.Message.TotalCapacity, context.Message.CatalogVersion,
            context.Message.PricingTiers, context.CancellationToken);
        await SalesProjectionNotifications.PublishSalesChangedAsync(
            context, context.Message.EventId, "event-created",
            context.Message.CorrelationId, timeProvider.GetUtcNow());
        logger.LogInformation(
            "Applied {MessageType} to reporting for event {EventId} with correlation {CorrelationId}",
            nameof(EventCreatedV1), context.Message.EventId, context.Message.CorrelationId);
    }
}

public sealed class EventUpdatedConsumer(
    ReportingDbContext db,
    TimeProvider timeProvider,
    ILogger<EventUpdatedConsumer> logger) : IConsumer<EventUpdatedV1>
{
    public async Task Consume(ConsumeContext<EventUpdatedV1> context)
    {
        await SalesProjectionWriter.UpsertEventAsync(db, context.Message.EventId, context.Message.Name,
            context.Message.StartsAtUtc, context.Message.TotalCapacity, context.Message.CatalogVersion,
            context.Message.PricingTiers, context.CancellationToken);
        await SalesProjectionNotifications.PublishSalesChangedAsync(
            context, context.Message.EventId, "event-updated",
            context.Message.CorrelationId, timeProvider.GetUtcNow());
        logger.LogInformation(
            "Applied {MessageType} to reporting for event {EventId} with correlation {CorrelationId}",
            nameof(EventUpdatedV1), context.Message.EventId, context.Message.CorrelationId);
    }
}

public sealed class EventDeletedConsumer(
    ReportingDbContext db,
    TimeProvider timeProvider,
    ILogger<EventDeletedConsumer> logger) : IConsumer<EventDeletedV1>
{
    public async Task Consume(ConsumeContext<EventDeletedV1> context)
    {
        var projection = await db.EventSales.Include(item => item.PricingTiers)
            .SingleOrDefaultAsync(item => item.EventId == context.Message.EventId,
                context.CancellationToken);
        if (projection is not null && projection.CatalogVersion >= context.Message.CatalogVersion)
        {
            return;
        }

        if (projection is null)
        {
            projection = new EventSalesProjection
            {
                EventId = context.Message.EventId,
                EventName = "Deleted event"
            };
            db.EventSales.Add(projection);
        }

        projection.CatalogVersion = context.Message.CatalogVersion;
        projection.IsActive = false;
        projection.PricingTiers.ForEach(tier => tier.IsActive = false);
        await db.SaveChangesAsync(context.CancellationToken);
        await SalesProjectionNotifications.PublishSalesChangedAsync(
            context, context.Message.EventId, "event-deleted",
            context.Message.CorrelationId, timeProvider.GetUtcNow());
        logger.LogInformation(
            "Applied {MessageType} to reporting for event {EventId} with correlation {CorrelationId}",
            nameof(EventDeletedV1), context.Message.EventId, context.Message.CorrelationId);
    }
}

file static class SalesProjectionNotifications
{
    public static Task PublishSalesChangedAsync<T>(
        ConsumeContext<T> context,
        Guid eventId,
        string changeType,
        Guid correlationId,
        DateTimeOffset occurredAtUtc)
        where T : class
    {
        var message = new SalesProjectionChangedV1(
            eventId, changeType, correlationId, occurredAtUtc);
        return context.Publish(message, publish => publish.CorrelationId = correlationId,
            context.CancellationToken);
    }
}

internal static class SalesProjectionWriter
{
    public static async Task UpsertEventAsync(
        ReportingDbContext db,
        Guid eventId,
        string name,
        DateTimeOffset startsAtUtc,
        int totalCapacity,
        long catalogVersion,
        IReadOnlyList<PricingTierContract> incomingTiers,
        CancellationToken cancellationToken)
    {
        var projection = await db.EventSales.Include(item => item.PricingTiers)
            .SingleOrDefaultAsync(item => item.EventId == eventId, cancellationToken);
        if (projection is not null && projection.CatalogVersion >= catalogVersion)
        {
            return;
        }

        if (projection is null)
        {
            projection = new EventSalesProjection
            {
                EventId = eventId,
                EventName = name,
                StartsAtUtc = startsAtUtc,
                TotalCapacity = totalCapacity,
                CatalogVersion = catalogVersion,
                PricingTiers = incomingTiers.Select(ToProjection).ToList()
            };
            db.EventSales.Add(projection);
        }
        else
        {
            projection.EventName = name;
            projection.StartsAtUtc = startsAtUtc;
            projection.TotalCapacity = Math.Max(totalCapacity, projection.TicketsSold);
            projection.CatalogVersion = catalogVersion;
            projection.IsActive = true;

            var incomingById = incomingTiers.ToDictionary(tier => tier.TierId);
            foreach (var existing in projection.PricingTiers)
            {
                if (incomingById.TryGetValue(existing.PricingTierId, out var incoming))
                {
                    existing.Name = incoming.Name;
                    existing.Capacity = Math.Max(incoming.Capacity, existing.TicketsSold);
                    existing.IsActive = true;
                }
                else
                {
                    existing.IsActive = false;
                }
            }

            var existingIds = projection.PricingTiers.Select(tier => tier.PricingTierId).ToHashSet();
            foreach (var incoming in incomingTiers.Where(tier => !existingIds.Contains(tier.TierId)))
            {
                var added = ToProjection(incoming);
                projection.PricingTiers.Add(added);
                db.TierSales.Add(added);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static TierSalesProjection ToProjection(PricingTierContract tier) => new()
    {
        PricingTierId = tier.TierId,
        Name = tier.Name,
        Capacity = tier.Capacity
    };
}
