using EventTicketing.Contracts.Messages;
using EventTicketing.ServiceDefaults;
using EventTicketing.ServiceDefaults.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Reporting.Api.Consumers;
using Reporting.Api.Data;

var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--migrate").ToArray());
builder.AddPlatformDefaults("Reporting API");

builder.Services.AddControllers();
builder.Services.AddDbContext<ReportingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ReportingDbContext>(tags: ["ready"]);
builder.Services.AddAuthorization(options =>
    options.AddPolicy("report-reader", policy => policy.RequireRole("report-reader")));

builder.Services.AddMassTransit(registration =>
{
    registration.SetEndpointNameFormatter(
        new KebabCaseEndpointNameFormatter("reporting", includeNamespace: false));
    registration.AddConsumer<EventCreatedConsumer>();
    registration.AddConsumer<EventUpdatedConsumer>();
    registration.AddConsumer<EventDeletedConsumer>();
    registration.AddConsumer<TicketsPurchasedConsumer>();
    registration.AddEntityFrameworkOutbox<ReportingDbContext>(outbox => outbox.UsePostgres());
    registration.AddConfigureEndpointsCallback((context, _, endpoint) =>
    {
        endpoint.UseMessageRetry(retry => retry.Exponential(
            5, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(500)));
        endpoint.UseEntityFrameworkOutbox<ReportingDbContext>(context);
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
        rabbit.Message<SalesProjectionChangedV1>(topology =>
            topology.SetEntityName("sales-projection-changed-v1"));
        rabbit.ConfigureEndpoints(context);
    });
});

var app = builder.Build();
if (await app.InitializeDatabaseAsync<ReportingDbContext>(args)) return;
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
