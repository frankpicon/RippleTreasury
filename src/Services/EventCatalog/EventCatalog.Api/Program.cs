using EventCatalog.Api.Data;
using EventCatalog.Api.Services;
using EventTicketing.Contracts.Messages;
using EventTicketing.ServiceDefaults;
using EventTicketing.ServiceDefaults.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--migrate").ToArray());
builder.AddPlatformDefaults("Event Catalog API");

builder.Services.AddControllers();
builder.Services.AddScoped<IEventCatalogService, EventCatalogService>();
builder.Services.AddDbContext<EventCatalogDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<EventCatalogDbContext>(tags: ["ready"]);

builder.Services.AddAuthorization(options =>
    options.AddPolicy("event-admin", policy => policy.RequireRole("event-admin")));

builder.Services.AddMassTransit(registration =>
{
    registration.SetKebabCaseEndpointNameFormatter();
    registration.AddEntityFrameworkOutbox<EventCatalogDbContext>(outbox =>
    {
        outbox.UsePostgres();
        outbox.UseBusOutbox();
        outbox.QueryDelay = TimeSpan.FromMilliseconds(250);
    });

    registration.UsingRabbitMq((context, rabbit) =>
    {
        var settings = context.GetRequiredService<IConfiguration>().GetSection("RabbitMq");
        rabbit.Host(settings["Host"] ?? "localhost",
            settings.GetValue<ushort>("Port", 5672),
            settings["VirtualHost"] ?? "/",
            host =>
            {
                host.Username(settings["Username"] ?? "guest");
                host.Password(settings["Password"] ?? "guest");
            });
        rabbit.Message<EventCreatedV1>(topology => topology.SetEntityName("event-created-v1"));
        rabbit.Message<EventUpdatedV1>(topology => topology.SetEntityName("event-updated-v1"));
        rabbit.Message<EventDeletedV1>(topology => topology.SetEntityName("event-deleted-v1"));
        rabbit.UseMessageRetry(retry => retry.Exponential(
            5, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(500)));
        rabbit.ConfigureEndpoints(context);
    });
});

var app = builder.Build();
if (await app.InitializeDatabaseAsync<EventCatalogDbContext>(args)) return;
app.UsePlatformDefaults();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.Run();

public partial class Program
{
}
