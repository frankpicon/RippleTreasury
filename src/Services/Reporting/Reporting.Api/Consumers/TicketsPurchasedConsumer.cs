using EventTicketing.Contracts.Messages;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Reporting.Api.Data;
using Reporting.Api.Domain;

namespace Reporting.Api.Consumers;

public sealed class TicketsPurchasedConsumer(
    ReportingDbContext db,
    TimeProvider timeProvider,
    ILogger<TicketsPurchasedConsumer> logger) : IConsumer<TicketsPurchasedV1>
{
    public async Task Consume(ConsumeContext<TicketsPurchasedV1> context)
    {
        var message = context.Message;
        await db.LockEventAsync(message.EventId, context.CancellationToken);
        var eventRowsUpdated = await db.EventSales
            .Where(item => item.EventId == message.EventId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.TicketsSold,
                    item => item.TicketsSold + message.Quantity)
                .SetProperty(item => item.GrossRevenue,
                    item => item.GrossRevenue + message.TotalPrice)
                .SetProperty(item => item.TotalCapacity,
                    item => item.TotalCapacity >= item.TicketsSold + message.Quantity
                        ? item.TotalCapacity
                        : item.TicketsSold + message.Quantity),
                context.CancellationToken);

        if (eventRowsUpdated == 0)
        {
            db.EventSales.Add(new EventSalesProjection
            {
                EventId = message.EventId,
                EventName = "Pending event details",
                TotalCapacity = message.Quantity,
                TicketsSold = message.Quantity,
                GrossRevenue = message.TotalPrice,
                PricingTiers =
                [
                    new TierSalesProjection
                    {
                        PricingTierId = message.PricingTierId,
                        Name = message.PricingTierName,
                        Capacity = message.Quantity,
                        TicketsSold = message.Quantity,
                        GrossRevenue = message.TotalPrice
                    }
                ]
            });
            await db.SaveChangesAsync(context.CancellationToken);
            await PublishSalesChangedAsync(context, message, timeProvider.GetUtcNow());
            LogProcessed(message);
            return;
        }

        var tierRowsUpdated = await db.TierSales
            .Where(item => item.PricingTierId == message.PricingTierId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.TicketsSold,
                    item => item.TicketsSold + message.Quantity)
                .SetProperty(item => item.GrossRevenue,
                    item => item.GrossRevenue + message.TotalPrice)
                .SetProperty(item => item.Capacity,
                    item => item.Capacity >= item.TicketsSold + message.Quantity
                        ? item.Capacity
                        : item.TicketsSold + message.Quantity),
                context.CancellationToken);

        if (tierRowsUpdated == 0)
        {
            db.TierSales.Add(new TierSalesProjection
            {
                PricingTierId = message.PricingTierId,
                EventId = message.EventId,
                Name = message.PricingTierName,
                Capacity = message.Quantity,
                TicketsSold = message.Quantity,
                GrossRevenue = message.TotalPrice
            });
        }

        await db.SaveChangesAsync(context.CancellationToken);
        await PublishSalesChangedAsync(context, message, timeProvider.GetUtcNow());
        LogProcessed(message);
    }

    private static Task PublishSalesChangedAsync(
        ConsumeContext<TicketsPurchasedV1> context,
        TicketsPurchasedV1 source,
        DateTimeOffset occurredAtUtc)
    {
        var message = new SalesProjectionChangedV1(
            source.EventId, "tickets-purchased", source.CorrelationId, occurredAtUtc);
        return context.Publish(message, publish => publish.CorrelationId = source.CorrelationId,
            context.CancellationToken);
    }

    private void LogProcessed(TicketsPurchasedV1 message) =>
        logger.LogInformation(
            "Applied {MessageType} to reporting for purchase {PurchaseId}, event {EventId}, correlation {CorrelationId}",
            nameof(TicketsPurchasedV1), message.PurchaseId, message.EventId, message.CorrelationId);
}
