namespace Ticketing.Api.Domain;

public sealed class InventoryEvent
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public int TotalCapacity { get; set; }
    public long CatalogVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public List<InventoryTier> PricingTiers { get; set; } = [];
}

public sealed class InventoryTier
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public int Capacity { get; set; }
    public int TicketsSold { get; set; }
    public bool IsActive { get; set; } = true;
    public InventoryEvent Event { get; set; } = null!;
}

public sealed class TicketPurchase
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid PricingTierId { get; set; }
    public required string PricingTierName { get; set; }
    public required string CustomerEmail { get; set; }
    public required string BuyerSubject { get; set; }
    public required string IdempotencyKey { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTimeOffset PurchasedAtUtc { get; set; }
}
