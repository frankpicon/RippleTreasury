namespace Reporting.Api.Domain;

public sealed class EventSalesProjection
{
    public Guid EventId { get; set; }
    public required string EventName { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public int TotalCapacity { get; set; }
    public int TicketsSold { get; set; }
    public decimal GrossRevenue { get; set; }
    public long CatalogVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public List<TierSalesProjection> PricingTiers { get; set; } = [];
}

public sealed class TierSalesProjection
{
    public Guid PricingTierId { get; set; }
    public Guid EventId { get; set; }
    public required string Name { get; set; }
    public int Capacity { get; set; }
    public int TicketsSold { get; set; }
    public decimal GrossRevenue { get; set; }
    public bool IsActive { get; set; } = true;
    public EventSalesProjection Event { get; set; } = null!;
}
