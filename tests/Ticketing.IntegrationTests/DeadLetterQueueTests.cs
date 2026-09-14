using MassTransit;
using Xunit;

namespace Ticketing.IntegrationTests;

public sealed class DeadLetterQueueTests(TicketingFactory factory)
    : IClassFixture<TicketingFactory>
{
    [Fact]
    public async Task Permanently_failing_consumer_moves_message_to_error_queue()
    {
        var queueName = $"dead-letter-test-{NewId.NextGuid():N}";
        var errorQueueName = $"{queueName}_error";
        var attempts = 0;
        var faultedMessage =
            new TaskCompletionSource<ConsumeContext<PoisonMessage>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var bus = Bus.Factory.CreateUsingRabbitMq(configurator =>
        {
            configurator.Host(factory.RabbitMqHost, factory.RabbitMqPort, "/", host =>
            {
                host.Username("guest");
                host.Password("guest");
            });

            configurator.ReceiveEndpoint(queueName, endpoint =>
            {
                endpoint.UseMessageRetry(retry => retry.Immediate(2));
                endpoint.Handler<PoisonMessage>(_ =>
                {
                    Interlocked.Increment(ref attempts);
                    throw new InvalidOperationException("Intentional poison-message failure.");
                });
            });

            configurator.ReceiveEndpoint(errorQueueName, endpoint =>
            {
                endpoint.ConfigureConsumeTopology = false;
                endpoint.Handler<PoisonMessage>(context =>
                {
                    faultedMessage.TrySetResult(context);
                    return Task.CompletedTask;
                });
            });
        });

        var messageId = NewId.NextGuid();
        var correlationId = NewId.NextGuid();

        try
        {
            await bus.StartAsync();
            await bus.Publish(new PoisonMessage("fail"), context =>
            {
                context.MessageId = messageId;
                context.CorrelationId = correlationId;
            });

            var received = await faultedMessage.Task.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(3, Volatile.Read(ref attempts));
            Assert.Equal(messageId, received.MessageId);
            Assert.Equal(correlationId, received.CorrelationId);
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    public sealed record PoisonMessage(string Value);
}
