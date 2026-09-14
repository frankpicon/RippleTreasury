namespace EventCatalog.Api.Domain;

public sealed class EventEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Venue { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public int TotalCapacity { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<PricingTierEntity> PricingTiers { get; set; } = [];
}

public sealed class PricingTierEntity
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public int Capacity { get; set; }
    public EventEntity Event { get; set; } = null!;
}
