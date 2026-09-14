using EventTicketing.Contracts.Messages;
using MassTransit;

namespace Notifications.Worker.Consumers;

public sealed class MessageFaultConsumer(
    ILogger<MessageFaultConsumer> logger) :
    IConsumer<Fault<EventCreatedV1>>,
    IConsumer<Fault<EventUpdatedV1>>,
    IConsumer<Fault<EventDeletedV1>>,
    IConsumer<Fault<TicketsPurchasedV1>>,
    IConsumer<Fault<InventoryProjectionChangedV1>>,
    IConsumer<Fault<SalesProjectionChangedV1>>
{
    public Task Consume(ConsumeContext<Fault<EventCreatedV1>> context) => LogAsync(context);
    public Task Consume(ConsumeContext<Fault<EventUpdatedV1>> context) => LogAsync(context);
    public Task Consume(ConsumeContext<Fault<EventDeletedV1>> context) => LogAsync(context);
    public Task Consume(ConsumeContext<Fault<TicketsPurchasedV1>> context) => LogAsync(context);
    public Task Consume(ConsumeContext<Fault<InventoryProjectionChangedV1>> context) => LogAsync(context);
    public Task Consume(ConsumeContext<Fault<SalesProjectionChangedV1>> context) => LogAsync(context);

    private Task LogAsync<T>(ConsumeContext<Fault<T>> context)
        where T : class
    {
        var exceptions = context.Message.Exceptions
            .Select(exception => new { exception.ExceptionType, exception.Message })
            .ToArray();

        logger.LogError(
            "Message processing exhausted retries. MessageType {MessageType}, MessageId {MessageId}, " +
            "CorrelationId {CorrelationId}, FaultedAtUtc {FaultedAtUtc}, Exceptions {@Exceptions}, " +
            "RetryExhausted {RetryExhausted}",
            typeof(T).FullName,
            context.Message.FaultedMessageId,
            context.CorrelationId,
            context.Message.Timestamp,
            exceptions,
            true);

        return Task.CompletedTask;
    }
}
