using EventTicketing.Contracts.Messages;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Realtime.Api.Hubs;

namespace Realtime.Api.Consumers;

public sealed class CatalogChangedConsumer(
    IHubContext<RealtimeHub, IRealtimeClient> hub,
    ILogger<CatalogChangedConsumer> logger)
    : IConsumer<EventCreatedV1>, IConsumer<EventUpdatedV1>, IConsumer<EventDeletedV1>
{
    public Task Consume(ConsumeContext<EventCreatedV1> context) =>
        BroadcastAsync(context.Message.EventId, "event-created", context.Message.CorrelationId,
            context.Message.OccurredAtUtc, nameof(EventCreatedV1));

    public Task Consume(ConsumeContext<EventUpdatedV1> context) =>
        BroadcastAsync(context.Message.EventId, "event-updated", context.Message.CorrelationId,
            context.Message.OccurredAtUtc, nameof(EventUpdatedV1));

    public Task Consume(ConsumeContext<EventDeletedV1> context) =>
        BroadcastAsync(context.Message.EventId, "event-deleted", context.Message.CorrelationId,
            context.Message.OccurredAtUtc, nameof(EventDeletedV1));

    private async Task BroadcastAsync(
        Guid eventId,
        string changeType,
        Guid correlationId,
        DateTimeOffset occurredAtUtc,
        string messageType)
    {
        await hub.Clients.All.CatalogChanged(
            new RealtimeUpdate(eventId, changeType, correlationId, occurredAtUtc));
        logger.LogInformation(
            "Broadcast {SignalName} for {MessageType}, event {EventId}, correlation {CorrelationId}",
            nameof(IRealtimeClient.CatalogChanged), messageType, eventId, correlationId);
    }
}

public sealed class ProjectionChangedConsumer(
    IHubContext<RealtimeHub, IRealtimeClient> hub,
    ILogger<ProjectionChangedConsumer> logger)
    : IConsumer<InventoryProjectionChangedV1>, IConsumer<SalesProjectionChangedV1>
{
    public async Task Consume(ConsumeContext<InventoryProjectionChangedV1> context)
    {
        var message = context.Message;
        await hub.Clients.All.InventoryChanged(
            new RealtimeUpdate(message.EventId, message.ChangeType, message.CorrelationId,
                message.OccurredAtUtc));
        logger.LogInformation(
            "Broadcast {SignalName} for event {EventId}, correlation {CorrelationId}",
            nameof(IRealtimeClient.InventoryChanged), message.EventId, message.CorrelationId);
    }

    public async Task Consume(ConsumeContext<SalesProjectionChangedV1> context)
    {
        var message = context.Message;
        await hub.Clients.All.SalesChanged(
            new RealtimeUpdate(message.EventId, message.ChangeType, message.CorrelationId,
                message.OccurredAtUtc));
        logger.LogInformation(
            "Broadcast {SignalName} for event {EventId}, correlation {CorrelationId}",
            nameof(IRealtimeClient.SalesChanged), message.EventId, message.CorrelationId);
    }
}
