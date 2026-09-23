using EventCatalog.Api.Contracts;
using EventCatalog.Api.Domain;
using EventCatalog.Api.Services;
using EventTicketing.ServiceDefaults.Errors;
using Xunit;

namespace EventCatalog.UnitTests;

public sealed class TierReconciliationTests
{
    [Fact]
    public void Capacity_cannot_be_reduced_after_event_creation()
    {
        var tierId = Guid.NewGuid();
        var entity = CreateEvent(tierId, capacity: 100);
        var request = CreateUpdate(tierId, eventCapacity: 90, tierCapacity: 90);

        var error = Assert.Throws<ResourceConflictException>(() =>
            EventCatalogService.ValidateCapacityChange(entity, request));

        Assert.Contains("Event capacity cannot be reduced", error.Message);
    }

    [Fact]
    public void Existing_tier_cannot_be_reduced_or_removed_after_event_creation()
    {
        var tierId = Guid.NewGuid();
        var entity = CreateEvent(tierId, capacity: 100);
        var reduced = CreateUpdate(tierId, eventCapacity: 100, tierCapacity: 90,
            additionalCapacity: 10);
        var removed = CreateUpdate(null, eventCapacity: 100, tierCapacity: 100);

        Assert.Throws<ResourceConflictException>(() =>
            EventCatalogService.ValidateCapacityChange(entity, reduced));
        Assert.Throws<ResourceConflictException>(() =>
            EventCatalogService.ValidateCapacityChange(entity, removed));
    }

    [Fact]
    public void Capacity_can_be_increased_while_existing_tiers_are_preserved()
    {
        var tierId = Guid.NewGuid();
        var entity = CreateEvent(tierId, capacity: 100);
        var request = CreateUpdate(tierId, eventCapacity: 120, tierCapacity: 110,
            additionalCapacity: 10);

        EventCatalogService.ValidateCapacityChange(entity, request);
    }

    [Fact]
    public void Renaming_a_tier_preserves_its_identity()
    {
        var id = Guid.NewGuid();
        var tiers = new List<PricingTierEntity>
        {
            new() { Id = id, Name = "General", Price = 50, Capacity = 100 }
        };
        EventCatalogService.ReconcilePricingTiers(tiers,
            [new PricingTierRequest { Id = id, Name = "Standard", Price = 60, Capacity = 100 }]);
        var tier = Assert.Single(tiers);
        Assert.Equal(id, tier.Id);
        Assert.Equal("Standard", tier.Name);
    }

    [Fact]
    public void Foreign_or_duplicate_tier_ids_are_rejected_without_removing_existing_tiers()
    {
        var id = Guid.NewGuid();
        var tiers = new List<PricingTierEntity>
        {
            new() { Id = id, Name = "General", Price = 50, Capacity = 100 }
        };
        Assert.Throws<EventTicketing.ServiceDefaults.Errors.RequestValidationException>(() =>
            EventCatalogService.ReconcilePricingTiers(tiers,
                [new PricingTierRequest { Id = Guid.NewGuid(), Name = "Foreign", Capacity = 100 }]));
        Assert.Throws<EventTicketing.ServiceDefaults.Errors.RequestValidationException>(() =>
            EventCatalogService.ReconcilePricingTiers(tiers,
            [
                new PricingTierRequest { Id = id, Name = "One", Capacity = 50 },
                new PricingTierRequest { Id = id, Name = "Two", Capacity = 50 }
            ]));
        Assert.Equal(id, Assert.Single(tiers).Id);
    }

    [Fact]
    public void Missing_tiers_are_removed_and_new_tiers_receive_new_ids()
    {
        var generalId = Guid.NewGuid();
        var removedId = Guid.NewGuid();
        var tiers = new List<PricingTierEntity>
        {
            new() { Id = generalId, Name = "General", Price = 50, Capacity = 80 },
            new() { Id = removedId, Name = "Balcony", Price = 25, Capacity = 20 }
        };

        EventCatalogService.ReconcilePricingTiers(tiers,
        [
            new PricingTierRequest { Id = generalId, Name = "General", Price = 55, Capacity = 80 },
            new PricingTierRequest { Name = "VIP", Price = 150, Capacity = 20 }
        ]);

        Assert.Equal(2, tiers.Count);
        Assert.Equal(generalId, Assert.Single(tiers, tier => tier.Name == "General").Id);
        var vip = Assert.Single(tiers, tier => tier.Name == "VIP");
        Assert.NotEqual(Guid.Empty, vip.Id);
        Assert.DoesNotContain(tiers, tier => tier.Id == removedId);
    }

    private static EventEntity CreateEvent(Guid tierId, int capacity) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Event",
        Description = "Description",
        Venue = "Venue",
        StartsAtUtc = DateTimeOffset.UtcNow.AddDays(30),
        TotalCapacity = capacity,
        PricingTiers =
        [
            new PricingTierEntity
            {
                Id = tierId,
                Name = "General",
                Capacity = capacity
            }
        ]
    };

    private static UpdateEventRequest CreateUpdate(
        Guid? tierId,
        int eventCapacity,
        int tierCapacity,
        int additionalCapacity = 0) => new()
    {
        Name = "Event",
        Description = "Description",
        Venue = "Venue",
        StartsAtUtc = DateTimeOffset.UtcNow.AddDays(30),
        TotalCapacity = eventCapacity,
        Version = 1,
        PricingTiers = tierId.HasValue
            ?
            [
                new PricingTierRequest
                {
                    Id = tierId,
                    Name = "General",
                    Capacity = tierCapacity
                },
                .. additionalCapacity > 0
                    ? [new PricingTierRequest { Name = "New", Capacity = additionalCapacity }]
                    : Array.Empty<PricingTierRequest>()
            ]
            : [new PricingTierRequest { Name = "Replacement", Capacity = tierCapacity }]
    };
}
