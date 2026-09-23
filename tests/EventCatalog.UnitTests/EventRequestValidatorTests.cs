using EventCatalog.Api.Contracts;
using EventCatalog.Api.Domain;
using EventCatalog.Api.Services;
using EventTicketing.ServiceDefaults.Errors;
using Xunit;

namespace EventCatalog.UnitTests;

public sealed class EventRequestValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_event_is_accepted()
    {
        var request = CreateRequest(totalCapacity: 100,
            new PricingTierRequest { Name = "General", Price = 50, Capacity = 100 });

        EventRequestValidator.Validate(request, Now);
    }

    [Fact]
    public void Tier_capacity_must_equal_total_capacity()
    {
        var request = CreateRequest(totalCapacity: 100,
            new PricingTierRequest { Name = "General", Price = 50, Capacity = 99 });

        var exception = Assert.Throws<RequestValidationException>(() =>
            EventRequestValidator.Validate(request, Now));

        Assert.Contains("must equal", exception.Message);
    }

    [Fact]
    public void Tier_names_are_case_insensitively_unique()
    {
        var request = CreateRequest(totalCapacity: 100,
            new PricingTierRequest { Name = "VIP", Price = 100, Capacity = 50 },
            new PricingTierRequest { Name = "vip", Price = 90, Capacity = 50 });

        Assert.Throws<RequestValidationException>(() => EventRequestValidator.Validate(request, Now));
    }

    [Fact]
    public void Negative_price_is_rejected()
    {
        var request = CreateRequest(totalCapacity: 10,
            new PricingTierRequest { Name = "General", Price = -1, Capacity = 10 });

        Assert.Throws<RequestValidationException>(() => EventRequestValidator.Validate(request, Now));
    }

    [Fact]
    public void Event_update_preserves_the_identity_of_an_existing_tier()
    {
        var tierId = Guid.NewGuid();
        var tiers = new List<PricingTierEntity>
        {
            new() { Id = tierId, Name = "General", Price = 50, Capacity = 100 }
        };

        EventCatalogService.ReconcilePricingTiers(tiers,
        [
            new PricingTierRequest { Id = tierId, Name = "GENERAL", Price = 60, Capacity = 120 }
        ]);

        var tier = Assert.Single(tiers);
        Assert.Equal(tierId, tier.Id);
        Assert.Equal("GENERAL", tier.Name);
        Assert.Equal(60, tier.Price);
        Assert.Equal(120, tier.Capacity);
    }

    private static CreateEventRequest CreateRequest(
        int totalCapacity,
        params PricingTierRequest[] tiers) => new()
        {
            Name = "Architecture Summit",
            Description = "An event",
            Venue = "Grand Hall",
            StartsAtUtc = Now.AddDays(30),
            TotalCapacity = totalCapacity,
            PricingTiers = tiers.ToList()
        };
}
