namespace EventTicketing.Contracts.Messages;

public sealed record PricingTierContract(
    Guid TierId,
    string Name,
    decimal Price,
    int Capacity);

public sealed record EventCreatedV1(
    Guid EventId,
    string Name,
    string Description,
    string Venue,
    DateTimeOffset StartsAtUtc,
    int TotalCapacity,
    long CatalogVersion,
    IReadOnlyList<PricingTierContract> PricingTiers,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc);

public sealed record EventUpdatedV1(
    Guid EventId,
    string Name,
    string Description,
    string Venue,
    DateTimeOffset StartsAtUtc,
    int TotalCapacity,
    long CatalogVersion,
    IReadOnlyList<PricingTierContract> PricingTiers,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc);

public sealed record EventDeletedV1(
    Guid EventId,
    long CatalogVersion,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc);
