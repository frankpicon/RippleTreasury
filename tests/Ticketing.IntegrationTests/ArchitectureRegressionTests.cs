using System.Net;
using System.Net.Http.Json;
using EventTicketing.Contracts.Messages;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Api.Contracts;
using Ticketing.Api.Data;
using Ticketing.Api.Domain;
using Xunit;

namespace Ticketing.IntegrationTests;

public sealed class ArchitectureRegressionTests(TicketingFactory factory) : IClassFixture<TicketingFactory>
{
    [Fact]
    public async Task Delete_before_create_or_update_keeps_inventory_inactive()
    {
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();
        var messageIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await factory.Services.GetRequiredService<IBus>().Publish(
            new EventDeletedV1(id, 3, Guid.NewGuid(), DateTimeOffset.UtcNow), context => context.MessageId = messageIds[0]);
        await WaitForVersion(id, 3);
        var tiers = new[] { new PricingTierContract(Guid.NewGuid(), "General", 50, 100) };
        var bus = factory.Services.GetRequiredService<IBus>();
        await bus.Publish(new EventCreatedV1(id, "Late", "Description", "Venue",
            DateTimeOffset.UtcNow.AddDays(30), 100, 1, tiers, Guid.NewGuid(), DateTimeOffset.UtcNow),
            context => context.MessageId = messageIds[1]);
        await bus.Publish(new EventUpdatedV1(id, "Late update", "Description", "Venue",
            DateTimeOffset.UtcNow.AddDays(30), 100, 2, tiers, Guid.NewGuid(), DateTimeOffset.UtcNow),
            context => context.MessageId = messageIds[2]);
        // Wait for both old messages to reach their consumer inbox, not just the publish acknowledgment.
        await WaitForConsumedMessages(messageIds);
        var availability = await client.GetFromJsonAsync<AvailabilityResponse>($"/api/v1/events/{id}/availability");
        Assert.NotNull(availability);
        Assert.False(availability.IsActive);
        Assert.Equal(0, availability.Available);
    }

    [Fact]
    public async Task Replacing_a_sold_tier_does_not_create_additional_event_seats()
    {
        using var client = factory.CreateClient();
        var (id, _) = await Seed(100, 80);
        var replacement = Guid.NewGuid();
        await factory.Services.GetRequiredService<IBus>().Publish(new EventUpdatedV1(
            id, "Event", "Description", "Venue", DateTimeOffset.UtcNow.AddDays(30), 100, 2,
            [new PricingTierContract(replacement, "Replacement", 50, 100)], Guid.NewGuid(), DateTimeOffset.UtcNow));
        await WaitForVersion(id, 2);
        using var first = await Purchase(client, id, replacement, 20, 50);
        using var second = await Purchase(client, id, replacement, 1, 50);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var availability = await client.GetFromJsonAsync<AvailabilityResponse>($"/api/v1/events/{id}/availability");
        Assert.NotNull(availability);
        Assert.Equal(100, availability.TotalCapacity);
        Assert.Equal(100, availability.TicketsSold);
        Assert.Equal(0, availability.Available);
    }

    [Fact]
    public async Task Purchase_waits_for_catalog_transaction_and_rejects_old_price()
    {
        using var client = factory.CreateClient();
        var (id, tier) = await Seed(100, 0);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketingDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.LockEventAsync(id, default);
        await db.PricingTiers.Where(item => item.Id == tier)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Price, 60));
        var pending = Purchase(client, id, tier, 1, 50);
        await Task.Delay(200);
        Assert.False(pending.IsCompleted);
        await transaction.CommitAsync();
        using var response = await pending;
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await db.PricingTiers.Where(item => item.Id == tier).Select(item => item.TicketsSold).SingleAsync());
    }

    [Fact]
    public async Task Original_quote_can_be_replayed_after_price_changes_but_a_different_quote_cannot()
    {
        using var client = factory.CreateClient();
        var (id, tier) = await Seed(100, 0);
        var key = Guid.NewGuid().ToString();
        using var first = await Purchase(client, id, tier, 1, 50, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketingDbContext>();
        await db.PricingTiers.Where(item => item.Id == tier)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Price, 60));
        using var replay = await Purchase(client, id, tier, 1, 50, key);
        using var changed = await Purchase(client, id, tier, 1, 60, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal(1, await db.PricingTiers.Where(item => item.Id == tier).Select(item => item.TicketsSold).SingleAsync());
    }

    [Fact]
    public async Task Migrations_are_applied_and_reapplying_preserves_purchases()
    {
        using var client = factory.CreateClient();
        var (id, tier) = await Seed(100, 0);
        using var response = await Purchase(client, id, tier, 1, 50);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketingDbContext>();
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(1, await db.TicketPurchases.CountAsync(item => item.EventId == id));
    }

    private async Task<(Guid Event, Guid Tier)> Seed(int capacity, int sold)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketingDbContext>();
        var id = Guid.NewGuid();
        var tier = Guid.NewGuid();
        db.Events.Add(new InventoryEvent
        {
            Id = id, Name = "Event", StartsAtUtc = DateTimeOffset.UtcNow.AddDays(30),
            TotalCapacity = capacity, CatalogVersion = 1,
            PricingTiers = [new InventoryTier { Id = tier, Name = "General", Price = 50, Capacity = capacity, TicketsSold = sold }]
        });
        await db.SaveChangesAsync();
        return (id, tier);
    }

    private async Task WaitForVersion(Guid id, long version)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketingDbContext>();
            if (await db.Events.AnyAsync(item => item.Id == id && item.CatalogVersion == version, timeout.Token)) return;
            await Task.Delay(50, timeout.Token);
        }
    }

    private async Task WaitForConsumedMessages(Guid[] messageIds)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TicketingDbContext>();
            if (await db.Set<InboxState>().CountAsync(item => messageIds.Contains(item.MessageId) &&
                    item.Consumed != null, timeout.Token) == messageIds.Length) return;
            await Task.Delay(50, timeout.Token);
        }
    }

    private static async Task<HttpResponseMessage> Purchase(HttpClient client, Guid id, Guid tier, int quantity,
        decimal price, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/events/{id}/tickets")
        {
            Content = JsonContent.Create(new PurchaseTicketsRequest
            {
                PricingTierId = tier, Quantity = quantity, CustomerEmail = "buyer@example.com", ExpectedUnitPrice = price
            })
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }
}
