extern alias catalog;
extern alias notifications;
extern alias reporting;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using EventTicketing.ServiceDefaults.Persistence;
using Testcontainers.PostgreSql;
using Ticketing.Api.Data;
using Xunit;
using CatalogDb = catalog::EventCatalog.Api.Data.EventCatalogDbContext;
using NotificationsDb = notifications::Notifications.Worker.Data.NotificationsDbContext;
using ReportingDb = reporting::Reporting.Api.Data.ReportingDbContext;

namespace Ticketing.IntegrationTests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task Production_requires_an_explicit_migration_step()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await postgres.StartAsync();
        using var host = new HostBuilder().UseEnvironment("Production")
            .ConfigureServices(services => services.AddDbContext<TicketingDbContext>(options =>
                options.UseNpgsql(postgres.GetConnectionString())))
            .Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.InitializeDatabaseAsync<TicketingDbContext>([]));
        Assert.True(await host.InitializeDatabaseAsync<TicketingDbContext>(["--migrate"]));
        Assert.False(await host.InitializeDatabaseAsync<TicketingDbContext>([]));
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("ticketing")]
    [InlineData("reporting")]
    [InlineData("notifications")]
    public async Task Initial_schema_matches_model_and_can_be_applied_repeatedly(string service)
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var connection = postgres.GetConnectionString();
        await using DbContext db = service switch
        {
            "catalog" => new CatalogDb(new DbContextOptionsBuilder<CatalogDb>().UseNpgsql(connection).Options),
            "ticketing" => new TicketingDbContext(new DbContextOptionsBuilder<TicketingDbContext>().UseNpgsql(connection).Options),
            "reporting" => new ReportingDb(new DbContextOptionsBuilder<ReportingDb>().UseNpgsql(connection).Options),
            "notifications" => new NotificationsDb(new DbContextOptionsBuilder<NotificationsDb>().UseNpgsql(connection).Options),
            _ => throw new ArgumentException("Unknown service", nameof(service))
        };
        Assert.False(db.Database.HasPendingModelChanges());
        await db.Database.MigrateAsync();
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        // Exercise the actual relational tables, including the MassTransit inbox/outbox schema.
        await db.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM \"InboxState\"");
        await db.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM \"OutboxMessage\"");
        await db.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM \"OutboxState\"");
    }
}
