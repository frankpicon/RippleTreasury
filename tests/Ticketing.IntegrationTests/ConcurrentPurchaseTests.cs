using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Api.Contracts;
using Ticketing.Api.Data;
using Ticketing.Api.Domain;
using Xunit;

namespace Ticketing.IntegrationTests;

public sealed class ConcurrentPurchaseTests(TicketingFactory factory) : IClassFixture<TicketingFactory>
{
    [Fact]
    public async Task Concurrent_requests_do_not_oversell_inventory()
    {
        var eventId = Guid.NewGuid();
        var tierId = Guid.NewGuid();
        await SeedInventoryAsync(eventId, tierId, capacity: 5);
        using var client = factory.CreateClient();

        var attempts = Enumerable.Range(0, 10)
            .Select(index => PurchaseAsync(client, eventId, tierId, index));
        var statuses = await Task.WhenAll(attempts);

        Assert.Equal(5, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(5, statuses.Count(status => status == HttpStatusCode.Conflict));

        var availability = Assert.IsType<AvailabilityResponse>(
            await client.GetFromJsonAsync<AvailabilityResponse>(
                $"/api/v1/events/{eventId}/availability"));
        Assert.Equal(5, availability.TicketsSold);
        Assert.Equal(0, availability.Available);
    }

    [Fact]
    public async Task Repeated_idempotency_key_returns_the_original_purchase()
    {
        var eventId = Guid.NewGuid();
        var tierId = Guid.NewGuid();
        await SeedInventoryAsync(eventId, tierId, capacity: 2);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid().ToString("D");

        using var first = await SendPurchaseAsync(client, eventId, tierId, "same@example.com", key);
        using var second = await SendPurchaseAsync(client, eventId, tierId, "same@example.com", key);
        var firstPurchase = Assert.IsType<PurchaseResponse>(
            await first.Content.ReadFromJsonAsync<PurchaseResponse>());
        var secondPurchase = Assert.IsType<PurchaseResponse>(
            await second.Content.ReadFromJsonAsync<PurchaseResponse>());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal("false", first.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal("true", second.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(firstPurchase.PurchaseId, secondPurchase.PurchaseId);

        var availability = Assert.IsType<AvailabilityResponse>(
            await client.GetFromJsonAsync<AvailabilityResponse>(
                $"/api/v1/events/{eventId}/availability"));
        Assert.Equal(1, availability.TicketsSold);
    }

    [Fact]
    public async Task Reusing_idempotency_key_with_different_payload_returns_conflict()
    {
        var eventId = Guid.NewGuid();
        var tierId = Guid.NewGuid();
        await SeedInventoryAsync(eventId, tierId, capacity: 3);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid().ToString("D");

        using var first = await SendPurchaseAsync(client, eventId, tierId,
            "original@example.com", key);
        using var mismatched = await SendPurchaseAsync(client, eventId, tierId,
            "different@example.com", key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, mismatched.StatusCode);

        var availability = Assert.IsType<AvailabilityResponse>(
            await client.GetFromJsonAsync<AvailabilityResponse>(
                $"/api/v1/events/{eventId}/availability"));
        Assert.Equal(1, availability.TicketsSold);
    }

    [Fact]
    public async Task Concurrent_retries_with_one_idempotency_key_create_one_purchase()
    {
        var eventId = Guid.NewGuid();
        var tierId = Guid.NewGuid();
        await SeedInventoryAsync(eventId, tierId, capacity: 5);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid().ToString("D");

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => SendPurchaseAsync(client, eventId, tierId, "retry@example.com", key)));

        try
        {
            Assert.All(responses,
                response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            var purchases = await Task.WhenAll(responses.Select(response =>
                response.Content.ReadFromJsonAsync<PurchaseResponse>()));
            Assert.Single(purchases.Select(purchase => purchase!.PurchaseId).Distinct());

            var availability = Assert.IsType<AvailabilityResponse>(
                await client.GetFromJsonAsync<AvailabilityResponse>(
                    $"/api/v1/events/{eventId}/availability"));
            Assert.Equal(1, availability.TicketsSold);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Inventory_summary_returns_remaining_inventory_for_all_events()
    {
        var firstEventId = Guid.NewGuid();
        var firstTierId = Guid.NewGuid();
        var secondEventId = Guid.NewGuid();
        var secondTierId = Guid.NewGuid();
        await SeedInventoryAsync(firstEventId, firstTierId, capacity: 3);
        await SeedInventoryAsync(secondEventId, secondTierId, capacity: 7);
        using var client = factory.CreateClient();

        using var purchase = await SendPurchaseAsync(client, firstEventId, firstTierId,
            "summary@example.com", Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.Created, purchase.StatusCode);

        var summaries = Assert.IsAssignableFrom<IReadOnlyList<InventorySummaryResponse>>(
            await client.GetFromJsonAsync<List<InventorySummaryResponse>>(
                "/api/v1/events/availability"));
        var first = Assert.Single(summaries, summary => summary.EventId == firstEventId);
        var second = Assert.Single(summaries, summary => summary.EventId == secondEventId);

        Assert.Equal(3, first.TotalCapacity);
        Assert.Equal(1, first.TicketsSold);
        Assert.Equal(2, first.Available);
        Assert.Equal(7, second.Available);
    }

    private async Task SeedInventoryAsync(Guid eventId, Guid tierId, int capacity)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketingDbContext>();
        db.Events.Add(new InventoryEvent
        {
            Id = eventId,
            Name = "Concurrency Test",
            StartsAtUtc = DateTimeOffset.UtcNow.AddDays(30),
            TotalCapacity = capacity,
            CatalogVersion = 1,
            PricingTiers =
            [
                new InventoryTier
                {
                    Id = tierId,
                    Name = "General",
                    Price = 50,
                    Capacity = capacity
                }
            ]
        });
        await db.SaveChangesAsync();
    }

    private static async Task<HttpStatusCode> PurchaseAsync(
        HttpClient client,
        Guid eventId,
        Guid tierId,
        int index)
    {
        using var response = await SendPurchaseAsync(client, eventId, tierId,
            $"buyer{index}@example.com", Guid.NewGuid().ToString("D"));
        return response.StatusCode;
    }

    private static async Task<HttpResponseMessage> SendPurchaseAsync(
        HttpClient client,
        Guid eventId,
        Guid tierId,
        string email,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/events/{eventId}/tickets")
        {
            Content = JsonContent.Create(new PurchaseTicketsRequest
            {
                PricingTierId = tierId,
                ExpectedUnitPrice = 50,
                CustomerEmail = email,
                Quantity = 1
            })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }
}
