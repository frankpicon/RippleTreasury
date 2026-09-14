namespace EventTicketing.Contracts.Messages;

public sealed record TicketsPurchasedV1(
    Guid PurchaseId,
    Guid EventId,
    Guid PricingTierId,
    string PricingTierName,
    string CustomerEmail,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    Guid CorrelationId,
    DateTimeOffset PurchasedAtUtc);
