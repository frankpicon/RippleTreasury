using EventTicketing.ServiceDefaults;
using EventTicketing.ServiceDefaults.Correlation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformDefaults("API Gateway");
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseWebSockets();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapReverseProxy();
app.Run();

public partial class Program
{
}
