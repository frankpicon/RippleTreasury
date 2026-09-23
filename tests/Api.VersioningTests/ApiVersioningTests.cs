using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Api.VersioningTests;

public sealed class ApiVersioningTests(VersioningHost host) : IClassFixture<VersioningHost>
{
    private static string Path(string template) => template
        .Replace("{eventId}", VersioningHost.EventId.ToString())
        .Replace("{purchaseId}", VersioningHost.PurchaseId.ToString());

    private static HttpRequestMessage Request(HttpMethod method, string path, string? roles = "event-admin,ticket-buyer,report-reader")
    {
        var request = new HttpRequestMessage(method, Path(path));
        if (roles is not null) request.Headers.Add("X-Test-Roles", roles);
        return request;
    }

    [Theory]
    [InlineData("events")]
    [InlineData("events/{eventId}")]
    [InlineData("events/availability")]
    [InlineData("events/{eventId}/availability")]
    [InlineData("events/{eventId}/tickets/{purchaseId}")]
    [InlineData("reports/events/{eventId}/sales")]
    public async Task V1_reads_succeed_and_unversioned_reads_are_not_found(string resource)
    {
        using var unversioned = await host.Client.SendAsync(Request(HttpMethod.Get, "/api/" + resource));
        using var v1 = await host.Client.SendAsync(Request(HttpMethod.Get, "/api/v1/" + resource));
        Assert.Equal(HttpStatusCode.NotFound, unversioned.StatusCode);
        Assert.Equal(HttpStatusCode.OK, v1.StatusCode);
        Assert.Contains("1.0", v1.Headers.GetValues("api-supported-versions"));
    }

    [Theory]
    [InlineData("events")]
    [InlineData("events/{eventId}")]
    [InlineData("events/availability")]
    [InlineData("events/{eventId}/availability")]
    [InlineData("events/{eventId}/tickets/{purchaseId}")]
    [InlineData("reports/events/{eventId}/sales")]
    public async Task Unsupported_version_does_not_fall_back_to_V1(string resource)
    {
        using var response = await host.Client.SendAsync(Request(HttpMethod.Get, "/api/v2/" + resource));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "events")]
    [InlineData("GET", "events/{eventId}")]
    [InlineData("POST", "events")]
    [InlineData("PUT", "events/{eventId}")]
    [InlineData("DELETE", "events/{eventId}?version=1")]
    [InlineData("POST", "events/{eventId}/tickets")]
    [InlineData("GET", "events/{eventId}/tickets/{purchaseId}")]
    [InlineData("GET", "events/availability")]
    [InlineData("GET", "events/{eventId}/availability")]
    [InlineData("GET", "reports/events/{eventId}/sales")]
    public async Task Unversioned_endpoints_are_not_registered(string method, string resource)
    {
        using var response = await host.Client.SendAsync(Request(new HttpMethod(method), "/api/" + resource));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1")]
    public async Task V1_requires_authentication_and_purchase_role(string prefix)
    {
        using var anonymous = await host.Client.SendAsync(Request(HttpMethod.Get, prefix + "/events", null));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var purchase = Request(HttpMethod.Post, prefix + "/events/{eventId}/tickets", "report-reader");
        purchase.Content = JsonContent.Create(new
        {
            pricingTierId = VersioningHost.TierId,
            expectedUnitPrice = 10,
            quantity = 1,
            customerEmail = "buyer@example.com"
        });
        using var forbidden = await host.Client.SendAsync(purchase);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var report = await host.Client.SendAsync(Request(HttpMethod.Get, prefix + "/reports/events/{eventId}/sales", "ticket-buyer"));
        Assert.Equal(HttpStatusCode.Forbidden, report.StatusCode);
        using var delete = await host.Client.SendAsync(Request(HttpMethod.Delete, prefix + "/events/{eventId}?version=1", "ticket-buyer"));
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1")]
    public async Task Create_update_delete_and_purchase_work_with_followable_V1_locations(string prefix)
    {
        var body = new { name = "Version test", description = "Description", venue = "Hall",
            startsAtUtc = "2030-01-01T00:00:00Z", totalCapacity = 10, version = 1,
            pricingTiers = new[] { new { name = "General", price = 10, capacity = 10 } } };
        using var create = Request(HttpMethod.Post, prefix + "/events");
        create.Content = JsonContent.Create(body);
        using var created = await host.Client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        Assert.Contains("/api/v1", created.Headers.Location.ToString());
        using var fetched = await host.Client.SendAsync(Request(HttpMethod.Get, created.Headers.Location.ToString()));
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        using var update = Request(HttpMethod.Put, prefix + "/events/{eventId}");
        update.Content = JsonContent.Create(body);
        using var updated = await host.Client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var deleted = await host.Client.SendAsync(Request(HttpMethod.Delete, prefix + "/events/{eventId}?version=1"));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var purchase = Request(HttpMethod.Post, prefix + "/events/{eventId}/tickets");
        purchase.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        purchase.Content = JsonContent.Create(new
        {
            pricingTierId = VersioningHost.TierId,
            expectedUnitPrice = 10,
            quantity = 2,
            customerEmail = "buyer@example.com"
        });
        using var purchased = await host.Client.SendAsync(purchase);
        Assert.Equal(HttpStatusCode.Created, purchased.StatusCode);
        Assert.NotNull(purchased.Headers.Location);
        Assert.Contains("/api/v1", purchased.Headers.Location.ToString());
        using var receipt = await host.Client.SendAsync(Request(HttpMethod.Get, purchased.Headers.Location.ToString()));
        Assert.Equal(await purchased.Content.ReadAsStringAsync(), await receipt.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Swagger_contains_only_concrete_V1_routes()
    {
        using var response = await host.Client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/events", out _));
        Assert.All(paths.EnumerateObject(), path => Assert.StartsWith("/api/v1/", path.Name));
        Assert.True(paths.TryGetProperty("/api/v1/events/{eventId}/tickets", out _));
        Assert.True(paths.TryGetProperty("/api/v1/reports/events/{eventId}/sales", out _));
        Assert.DoesNotContain("{version}", paths.ToString());
        Assert.DoesNotContain("/api/v2", paths.ToString());
    }
}
