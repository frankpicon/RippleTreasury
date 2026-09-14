using EventTicketing.Contracts.Messages;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Notifications.Worker.Data;
using Notifications.Worker.Domain;

namespace Notifications.Worker.Consumers;

public sealed class TicketsPurchasedNotificationConsumer(
    NotificationsDbContext db,
    ILogger<TicketsPurchasedNotificationConsumer> logger) : IConsumer<TicketsPurchasedV1>
{
    public async Task Consume(ConsumeContext<TicketsPurchasedV1> context)
    {
        var message = context.Message;
        if (await db.Notifications.AnyAsync(item => item.PurchaseId == message.PurchaseId,
                context.CancellationToken))
        {
            return;
        }

        var notification = new NotificationRecord
        {
            Id = Guid.NewGuid(),
            PurchaseId = message.PurchaseId,
            Recipient = message.CustomerEmail,
            Subject = "Your ticket purchase is confirmed",
            Body = $"Purchase {message.PurchaseId}: {message.Quantity} ticket(s) for " +
                   $"{message.PricingTierName}, total {message.TotalPrice:C}.",
            Status = "Simulated",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        db.Notifications.Add(notification);
        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation(
            "Simulated confirmation notification {NotificationId} for purchase {PurchaseId} to {Recipient} " +
            "with correlation {CorrelationId}",
            notification.Id, notification.PurchaseId, notification.Recipient, message.CorrelationId);
    }
}
