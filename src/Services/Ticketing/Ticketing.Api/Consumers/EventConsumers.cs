using EventTicketing.Contracts.Messages;
using MassTransit;
using Ticketing.Api.Services;

namespace Ticketing.Api.Consumers;

public sealed class EventCreatedConsumer(
    InventoryProjectionService projection,
    TimeProvider timeProvider,
    ILogger<EventCreatedConsumer> logger)
    : IConsumer<EventCreatedV1>
{
    public async Task Consume(ConsumeContext<EventCreatedV1> context)
    {
        await projection.ApplyAsync(context.Message, context.CancellationToken);
        await InventoryProjectionNotifications.PublishInventoryChangedAsync(
            context, context.Message.EventId, "event-created",
            context.Message.CorrelationId, timeProvider.GetUtcNow());
        logger.LogInformation(
            "Applied {MessageType} to ticket inventory for event {EventId} with correlation {CorrelationId}",
            nameof(EventCreatedV1), context.Message.EventId, context.Message.CorrelationId);
    }
}

public sealed class EventUpdatedConsumer(
    InventoryProjectionService projection,
    TimeProvider timeProvider,
    ILogger<EventUpdatedConsumer> logger)
    : IConsumer<EventUpdatedV1>
{
    public async Task Consume(ConsumeContext<EventUpdatedV1> context)
    {
        await projection.ApplyAsync(context.Message, context.CancellationToken);
        await InventoryProjectionNotifications.PublishInventoryChangedAsync(
            context, context.Message.EventId, "event-updated",
            context.Message.CorrelationId, timeProvider.GetUtcNow());
        logger.LogInformation(
            "Applied {MessageType} to ticket inventory for event {EventId} with correlation {CorrelationId}",
            nameof(EventUpdatedV1), context.Message.EventId, context.Message.CorrelationId);
    }
}

public sealed class EventDeletedConsumer(
    InventoryProjectionService projection,
    TimeProvider timeProvider,
    ILogger<EventDeletedConsumer> logger)
    : IConsumer<EventDeletedV1>
{
    public async Task Consume(ConsumeContext<EventDeletedV1> context)
    {
        await projection.DeleteAsync(context.Message, context.CancellationToken);
        await InventoryProjectionNotifications.PublishInventoryChangedAsync(
            context, context.Message.EventId, "event-deleted",
            context.Message.CorrelationId, timeProvider.GetUtcNow());
        logger.LogInformation(
            "Applied {MessageType} to ticket inventory for event {EventId} with correlation {CorrelationId}",
            nameof(EventDeletedV1), context.Message.EventId, context.Message.CorrelationId);
    }
}

file static class InventoryProjectionNotifications
{
    public static Task PublishInventoryChangedAsync<T>(
        ConsumeContext<T> context,
        Guid eventId,
        string changeType,
        Guid correlationId,
        DateTimeOffset occurredAtUtc)
        where T : class
    {
        var message = new InventoryProjectionChangedV1(
            eventId, changeType, correlationId, occurredAtUtc);
        return context.Publish(message, publish => publish.CorrelationId = correlationId,
            context.CancellationToken);
    }
}
