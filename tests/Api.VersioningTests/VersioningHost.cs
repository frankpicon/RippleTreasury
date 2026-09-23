using System.Security.Claims;
using System.Text.Encodings.Web;
using EventCatalog.Api.Controllers;
using EventTicketing.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reporting.Api.Controllers;
using Reporting.Api.Data;
using Reporting.Api.Domain;
using Ticketing.Api.Controllers;
using Xunit;

namespace Api.VersioningTests;

// Exercises real MVC routing, version selection, authorization and Swagger without
// external infrastructure. Database/concurrency correctness is covered separately.
public sealed class VersioningHost : IAsyncLifetime
{
    public static readonly Guid EventId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid TierId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid PurchaseId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private WebApplication _app = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = "Testing";
        builder.WebHost.UseTestServer();
        builder.Configuration["Authentication:Enabled"] = "false";
        builder.AddPlatformDefaults("API compatibility tests");
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(EventsController).Assembly)
            .AddApplicationPart(typeof(TicketsController).Assembly)
            .AddApplicationPart(typeof(ReportsController).Assembly);
        builder.Services.AddSingleton<EventCatalog.Api.Services.IEventCatalogService, CatalogStub>();
        builder.Services.AddSingleton<Ticketing.Api.Services.ITicketPurchaseService, TicketingStub>();
        builder.Services.AddDbContext<ReportingDbContext>(options => options.UseInMemoryDatabase("reports"));
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "Test";
            options.DefaultChallengeScheme = "Test";
            options.DefaultForbidScheme = "Test";
        }).AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Test", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            foreach (var role in new[] { "event-admin", "ticket-buyer", "report-reader" })
                options.AddPolicy(role, policy => policy.RequireRole(role));
        });
        _app = builder.Build();
        _app.UsePlatformDefaults();
        _app.MapControllers();
        await _app.StartAsync();
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        db.EventSales.Add(new EventSalesProjection
        {
            EventId = EventId, EventName = "Version test", TotalCapacity = 10,
            TicketsSold = 2, GrossRevenue = 20, StartsAtUtc = DateTimeOffset.Parse("2030-01-01T00:00:00Z")
        });
        await db.SaveChangesAsync();
        Client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}

internal sealed class HeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Roles", out var roles))
            return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new("sub", "test-buyer") };
        claims.AddRange(roles.ToString().Split(',').Select(role => new Claim(ClaimTypes.Role, role)));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
