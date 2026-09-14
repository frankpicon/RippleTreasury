using EventTicketing.Contracts.Messages;
using EventTicketing.ServiceDefaults;
using EventTicketing.ServiceDefaults.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Ticketing.Api.Consumers;
using Ticketing.Api.Data;
using Ticketing.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformDefaults("Ticketing API");

builder.Services.AddControllers();
builder.Services.AddScoped<ITicketPurchaseService, TicketPurchaseService>();
builder.Services.AddScoped<InventoryProjectionService>();
builder.Services.AddDbContext<TicketingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<TicketingDbContext>(tags: ["ready"]);

builder.Services.AddAuthorization(options =>
    options.AddPolicy("ticket-buyer", policy => policy.RequireRole("ticket-buyer")));

builder.Services.AddMassTransit(registration =>
{
    registration.SetEndpointNameFormatter(
        new KebabCaseEndpointNameFormatter("ticketing", includeNamespace: false));
    registration.AddConsumer<EventCreatedConsumer>();
    registration.AddConsumer<EventUpdatedConsumer>();
    registration.AddConsumer<EventDeletedConsumer>();
    registration.AddEntityFrameworkOutbox<TicketingDbContext>(outbox =>
    {
        outbox.UsePostgres();
        outbox.UseBusOutbox();
        outbox.QueryDelay = TimeSpan.FromMilliseconds(250);
    });
    registration.AddConfigureEndpointsCallback((context, _, endpoint) =>
    {
        endpoint.UseMessageRetry(retry => retry.Exponential(
            5, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(500)));
        endpoint.UseEntityFrameworkOutbox<TicketingDbContext>(context);
    });

    registration.UsingRabbitMq((context, rabbit) =>
    {
        var settings = context.GetRequiredService<IConfiguration>().GetSection("RabbitMq");
        rabbit.Host(settings["Host"] ?? "localhost", settings.GetValue<ushort>("Port", 5672),
            settings["VirtualHost"] ?? "/", host =>
        {
            host.Username(settings["Username"] ?? "guest");
            host.Password(settings["Password"] ?? "guest");
        });
        rabbit.Message<EventCreatedV1>(topology => topology.SetEntityName("event-created-v1"));
        rabbit.Message<EventUpdatedV1>(topology => topology.SetEntityName("event-updated-v1"));
        rabbit.Message<EventDeletedV1>(topology => topology.SetEntityName("event-deleted-v1"));
        rabbit.Message<TicketsPurchasedV1>(topology => topology.SetEntityName("tickets-purchased-v1"));
        rabbit.Message<InventoryProjectionChangedV1>(topology =>
            topology.SetEntityName("inventory-projection-changed-v1"));
        rabbit.ConfigureEndpoints(context);
    });
});

var app = builder.Build();
await app.EnsureDatabaseAsync<TicketingDbContext>();
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
