using System.Text.Json;
using EventTicketing.Contracts.Messages;
using EventTicketing.ServiceDefaults;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Realtime.Api.Consumers;
using Realtime.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformDefaults("Realtime API");

var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    .AddStackExchangeRedis(redisConnection);
builder.Services.AddHealthChecks();

builder.Services.AddMassTransit(registration =>
{
    registration.SetEndpointNameFormatter(
        new KebabCaseEndpointNameFormatter("realtime", includeNamespace: false));
    registration.AddConsumer<CatalogChangedConsumer>();
    registration.AddConsumer<ProjectionChangedConsumer>();
    registration.AddConfigureEndpointsCallback((_, _, endpoint) =>
        endpoint.UseMessageRetry(retry => retry.Exponential(
            5, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(500))));

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
        rabbit.Message<InventoryProjectionChangedV1>(topology =>
            topology.SetEntityName("inventory-projection-changed-v1"));
        rabbit.Message<SalesProjectionChangedV1>(topology =>
            topology.SetEntityName("sales-projection-changed-v1"));
        rabbit.ConfigureEndpoints(context);
    });
});

var app = builder.Build();
app.UsePlatformDefaults();
app.MapHub<RealtimeHub>("/hubs/updates").RequireAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.Run();

public partial class Program
{
}
