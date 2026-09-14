namespace EventTicketing.Contracts.Messages;

public sealed record InventoryProjectionChangedV1(
    Guid EventId,
    string ChangeType,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc);

public sealed record SalesProjectionChangedV1(
    Guid EventId,
    string ChangeType,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc);
