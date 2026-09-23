using EventTicketing.Contracts.Messages;
using EventTicketing.ServiceDefaults;
using EventTicketing.ServiceDefaults.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Notifications.Worker.Consumers;
using Notifications.Worker.Data;

var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--migrate").ToArray());
builder.AddPlatformDefaults("Notifications Worker");

builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<NotificationsDbContext>(tags: ["ready"]);

builder.Services.AddMassTransit(registration =>
{
    registration.SetEndpointNameFormatter(
        new KebabCaseEndpointNameFormatter("notifications", includeNamespace: false));
    registration.AddConsumer<TicketsPurchasedNotificationConsumer>();
    registration.AddConsumer<MessageFaultConsumer>();
    registration.AddEntityFrameworkOutbox<NotificationsDbContext>(outbox => outbox.UsePostgres());
    registration.AddConfigureEndpointsCallback((context, _, endpoint) =>
    {
        endpoint.UseMessageRetry(retry => retry.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
        endpoint.UseEntityFrameworkOutbox<NotificationsDbContext>(context);
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
        rabbit.Message<TicketsPurchasedV1>(topology => topology.SetEntityName("tickets-purchased-v1"));
        rabbit.ConfigureEndpoints(context);
    });
});

var app = builder.Build();
if (await app.InitializeDatabaseAsync<NotificationsDbContext>(args)) return;
app.UsePlatformDefaults();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.Run();

public partial class Program
{
}
