extern alias reporting;

using EventTicketing.Contracts.Messages;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;
using ReportingDbContext = reporting::Reporting.Api.Data.ReportingDbContext;

namespace Ticketing.IntegrationTests;

public sealed class ReportingRegressionTests
{
    [Fact]
    public async Task Projection_updates_preserve_deletions_sales_and_event_capacity()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await using var rabbit = new RabbitMqBuilder().WithImage("rabbitmq:4-management-alpine").Build();
        await Task.WhenAll(postgres.StartAsync(), rabbit.StartAsync());
        await using var factory = new WebApplicationFactory<reporting::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
                services.Configure<MassTransitHostOptions>(options => options.WaitUntilStarted = true));
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = postgres.GetConnectionString(),
                    ["RabbitMq:Host"] = rabbit.Hostname,
                    ["RabbitMq:Port"] = rabbit.GetMappedPublicPort(5672).ToString(),
                    ["RabbitMq:Username"] = "rabbitmq",
                    ["RabbitMq:Password"] = "rabbitmq",
                    ["Authentication:Enabled"] = "false",
                    ["OpenTelemetry:Endpoint"] = string.Empty
                }));
        });
        using var client = factory.CreateClient();
        var bus = factory.Services.GetRequiredService<IBus>();
        var eventId = Guid.NewGuid();
        var tierId = Guid.NewGuid();
        var messageIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        await bus.Publish(new EventDeletedV1(eventId, 3, Guid.NewGuid(), DateTimeOffset.UtcNow),
            context => context.MessageId = messageIds[0]);
        await WaitForMessages(factory.Services, [messageIds[0]]);
        var tiers = new[] { new PricingTierContract(tierId, "General", 50, 100) };
        await bus.Publish(new EventCreatedV1(eventId, "Late", "Description", "Venue",
            DateTimeOffset.UtcNow.AddDays(30), 100, 1, tiers, Guid.NewGuid(), DateTimeOffset.UtcNow),
            context => context.MessageId = messageIds[1]);
        await bus.Publish(new EventUpdatedV1(eventId, "Late update", "Description", "Venue",
            DateTimeOffset.UtcNow.AddDays(30), 100, 2, tiers, Guid.NewGuid(), DateTimeOffset.UtcNow),
            context => context.MessageId = messageIds[2]);
        await bus.Publish(new TicketsPurchasedV1(Guid.NewGuid(), eventId, tierId, "General",
            "buyer@example.com", 1, 50, 50, Guid.NewGuid(), DateTimeOffset.UtcNow),
            context => context.MessageId = messageIds[3]);
        await WaitForMessages(factory.Services, messageIds);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var projection = await db.EventSales.SingleAsync(item => item.EventId == eventId);
        Assert.False(projection.IsActive);
        Assert.Equal(3, projection.CatalogVersion);
        Assert.Equal(1, projection.TicketsSold);
        Assert.Equal(50, projection.GrossRevenue);
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.Equal(1, await db.EventSales.CountAsync(item => item.EventId == eventId));

        var activeEvent = Guid.NewGuid();
        var originalTier = Guid.NewGuid();
        var replacementTier = Guid.NewGuid();
        var replacementMessages = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        await bus.Publish(new EventCreatedV1(activeEvent, "Active", "Description", "Venue",
            DateTimeOffset.UtcNow.AddDays(30), 100, 1,
            [new PricingTierContract(originalTier, "General", 50, 100)], Guid.NewGuid(), DateTimeOffset.UtcNow),
            context => context.MessageId = replacementMessages[0]);
        await WaitForMessages(factory.Services, [replacementMessages[0]]);
        await bus.Publish(new TicketsPurchasedV1(Guid.NewGuid(), activeEvent, originalTier, "General",
            "buyer@example.com", 80, 50, 4000, Guid.NewGuid(), DateTimeOffset.UtcNow),
            context => context.MessageId = replacementMessages[1]);
        await WaitForMessages(factory.Services, [replacementMessages[1]]);
        await bus.Publish(new EventUpdatedV1(activeEvent, "Active", "Description", "Venue",
            DateTimeOffset.UtcNow.AddDays(30), 100, 2,
            [new PricingTierContract(replacementTier, "Replacement", 50, 100)], Guid.NewGuid(), DateTimeOffset.UtcNow),
            context => context.MessageId = replacementMessages[2]);
        await WaitForMessages(factory.Services, replacementMessages);
        var updated = await db.EventSales.AsNoTracking().Include(item => item.PricingTiers)
            .SingleAsync(item => item.EventId == activeEvent);
        Assert.Equal(100, updated.TotalCapacity);
        Assert.Equal(80, updated.TicketsSold);
        Assert.Equal(4000, updated.GrossRevenue);
        Assert.False(Assert.Single(updated.PricingTiers, item => item.PricingTierId == originalTier).IsActive);
        Assert.True(Assert.Single(updated.PricingTiers, item => item.PricingTierId == replacementTier).IsActive);
    }

    private static async Task WaitForMessages(IServiceProvider services, Guid[] ids)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            if (await db.Set<InboxState>().CountAsync(item => ids.Contains(item.MessageId) &&
                    item.Consumed != null, timeout.Token) == ids.Length) return;
            await Task.Delay(50, timeout.Token);
        }
    }
}
