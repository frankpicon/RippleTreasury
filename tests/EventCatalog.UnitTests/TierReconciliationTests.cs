using EventCatalog.Api.Contracts;
using EventCatalog.Api.Domain;
using EventCatalog.Api.Services;
using Xunit;

namespace EventCatalog.UnitTests;

public sealed class TierReconciliationTests
{
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
}
