using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Model;

namespace Api.VersioningTests;

public sealed class GatewayRoutingTests
{
    [Theory]
    [InlineData("GET", "events", "event-catalog")]
    [InlineData("POST", "events", "event-catalog")]
    [InlineData("PUT", "events/123", "event-catalog")]
    [InlineData("DELETE", "events/123", "event-catalog")]
    [InlineData("GET", "events/availability", "ticketing")]
    [InlineData("GET", "events/123/availability", "ticketing")]
    [InlineData("POST", "events/123/tickets", "ticketing")]
    [InlineData("GET", "events/123/tickets/456", "ticketing")]
    [InlineData("GET", "reports/events/123/sales", "reporting")]
    public async Task Production_routes_select_the_owning_service_and_preserve_version(
        string method, string resource, string cluster)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddJsonFile(System.IO.Path.Combine(AppContext.BaseDirectory, "gateway.json"));
        builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
        await using var app = builder.Build();
        // Stop after real YARP route selection; service contracts are exercised by ApiVersioningTests.
        app.MapReverseProxy(pipeline => pipeline.Run(context => context.Response.WriteAsync(
            context.GetReverseProxyFeature().Cluster.Config.ClusterId + "|" + context.Request.Path)));
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var unversioned = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), "/api/" + resource));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, unversioned.StatusCode);
        foreach (var prefix in new[] { "/api/v1/", "/api/v2/" })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), prefix + resource);
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            Assert.Equal(cluster + "|" + prefix + resource, await response.Content.ReadAsStringAsync());
        }
    }
}
