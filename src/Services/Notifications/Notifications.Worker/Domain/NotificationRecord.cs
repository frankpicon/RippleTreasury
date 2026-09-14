namespace Notifications.Worker.Domain;

public sealed class NotificationRecord
{
    public Guid Id { get; set; }
    public Guid PurchaseId { get; set; }
    public required string Recipient { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
