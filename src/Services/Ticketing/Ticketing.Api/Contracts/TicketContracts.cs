using System.ComponentModel.DataAnnotations;

namespace Ticketing.Api.Contracts;

public sealed class PurchaseTicketsRequest
{
    public Guid PricingTierId { get; init; }

    [Required, Range(typeof(decimal), "0.00", "1000000.00")]
    public decimal? ExpectedUnitPrice { get; init; }

    [Required, EmailAddress, StringLength(320)]
    public required string CustomerEmail { get; init; }

    [Range(1, 20)]
    public int Quantity { get; init; }
}

public sealed record PurchaseResponse(
    Guid PurchaseId,
    Guid EventId,
    Guid PricingTierId,
    string PricingTierName,
    string CustomerEmail,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    DateTimeOffset PurchasedAtUtc);

public sealed record TierAvailabilityResponse(
    Guid PricingTierId,
    string Name,
    decimal Price,
    int Capacity,
    int TicketsSold,
    int Available,
    bool IsActive);

public sealed record AvailabilityResponse(
    Guid EventId,
    string EventName,
    DateTimeOffset StartsAtUtc,
    int TotalCapacity,
    int TicketsSold,
    int Available,
    bool IsActive,
    IReadOnlyList<TierAvailabilityResponse> PricingTiers);

public sealed record InventorySummaryResponse(
    Guid EventId,
    int TotalCapacity,
    int TicketsSold,
    int Available,
    bool IsActive);
