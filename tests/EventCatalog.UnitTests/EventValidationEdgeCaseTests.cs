using EventCatalog.Api.Contracts;
using EventCatalog.Api.Services;
using EventTicketing.ServiceDefaults.Errors;
using Xunit;

namespace EventCatalog.UnitTests;

public sealed class EventValidationEdgeCaseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("", "An event", "Grand Hall")]
    [InlineData("Architecture Summit", "", "Grand Hall")]
    [InlineData("Architecture Summit", "An event", "")]
    public void Required_event_text_is_rejected(string name, string description, string venue)
    {
        var request = CreateRequest(name, description, venue, Now.AddDays(1), 10,
        [
            new PricingTierRequest { Name = "General", Price = 50, Capacity = 10 }
        ]);

        Assert.Throws<RequestValidationException>(() =>
            EventRequestValidator.Validate(request, Now));
    }

    [Fact]
    public void Event_must_start_in_the_future()
    {
        var request = CreateRequest("Event", "Description", "Venue", Now, 10,
        [
            new PricingTierRequest { Name = "General", Price = 50, Capacity = 10 }
        ]);

        Assert.Throws<RequestValidationException>(() =>
            EventRequestValidator.Validate(request, Now));
    }

    [Fact]
    public void Event_requires_at_least_one_pricing_tier()
    {
        var request = CreateRequest("Event", "Description", "Venue", Now.AddDays(1), 10, []);

        Assert.Throws<RequestValidationException>(() =>
            EventRequestValidator.Validate(request, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Total_capacity_must_be_positive(int capacity)
    {
        var request = CreateRequest("Event", "Description", "Venue", Now.AddDays(1), capacity,
        [
            new PricingTierRequest { Name = "General", Price = 0, Capacity = 1 }
        ]);

        Assert.Throws<RequestValidationException>(() =>
            EventRequestValidator.Validate(request, Now));
    }

    [Fact]
    public void Tier_capacity_must_be_positive()
    {
        var request = CreateRequest("Event", "Description", "Venue", Now.AddDays(1), 1,
        [
            new PricingTierRequest { Name = "General", Price = 0, Capacity = 0 }
        ]);

        Assert.Throws<RequestValidationException>(() =>
            EventRequestValidator.Validate(request, Now));
    }

    private static CreateEventRequest CreateRequest(
        string name,
        string description,
        string venue,
        DateTimeOffset startsAtUtc,
        int totalCapacity,
        List<PricingTierRequest> tiers) => new()
        {
            Name = name,
            Description = description,
            Venue = venue,
            StartsAtUtc = startsAtUtc,
            TotalCapacity = totalCapacity,
            PricingTiers = tiers
        };
}
