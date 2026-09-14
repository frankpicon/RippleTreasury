using EventCatalog.Api.Contracts;
using EventCatalog.Api.Domain;
using EventCatalog.Api.Services;
using Xunit;

namespace EventCatalog.UnitTests;

public sealed class TierReconciliationTests
{
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
            new PricingTierRequest { Name = "General", Price = 55, Capacity = 80 },
            new PricingTierRequest { Name = "VIP", Price = 150, Capacity = 20 }
        ]);

        Assert.Equal(2, tiers.Count);
        Assert.Equal(generalId, Assert.Single(tiers, tier => tier.Name == "General").Id);
        var vip = Assert.Single(tiers, tier => tier.Name == "VIP");
        Assert.NotEqual(Guid.Empty, vip.Id);
        Assert.DoesNotContain(tiers, tier => tier.Id == removedId);
    }
}
