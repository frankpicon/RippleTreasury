using System.ComponentModel.DataAnnotations;

namespace EventCatalog.Api.Contracts;

public sealed class PricingTierRequest
{
    // Omit for a new tier; preserve this ID when editing or renaming an existing tier.
    public Guid? Id { get; init; }

    [Required, StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    [Range(typeof(decimal), "0.00", "1000000.00")]
    public decimal Price { get; init; }

    [Range(1, 1_000_000)]
    public int Capacity { get; init; }
}

public class CreateEventRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Name { get; init; }

    [Required, StringLength(2_000, MinimumLength = 1)]
    public required string Description { get; init; }

    [Required, StringLength(300, MinimumLength = 1)]
    public required string Venue { get; init; }

    public DateTimeOffset StartsAtUtc { get; init; }

    [Range(1, 1_000_000)]
    public int TotalCapacity { get; init; }

    [Required, MinLength(1)]
    public required List<PricingTierRequest> PricingTiers { get; init; }
}

public sealed class UpdateEventRequest : CreateEventRequest
{
    [Range(1, long.MaxValue)]
    public long Version { get; init; }
}

public sealed record PricingTierResponse(Guid Id, string Name, decimal Price, int Capacity);

public sealed record EventResponse(
    Guid Id,
    string Name,
    string Description,
    string Venue,
    DateTimeOffset StartsAtUtc,
    int TotalCapacity,
    long Version,
    IReadOnlyList<PricingTierResponse> PricingTiers);
