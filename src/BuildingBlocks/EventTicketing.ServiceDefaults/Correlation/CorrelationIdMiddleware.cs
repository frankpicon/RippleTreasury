using Serilog.Context;

namespace EventTicketing.ServiceDefaults.Correlation;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    internal const string ItemName = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = Guid.TryParse(supplied, out var parsed) ? parsed : Guid.NewGuid();

        context.Items[ItemName] = correlationId;
        context.TraceIdentifier = correlationId.ToString("D");
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId.ToString("D");
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(ItemName, correlationId))
        {
            await next(context);
        }
    }
}
