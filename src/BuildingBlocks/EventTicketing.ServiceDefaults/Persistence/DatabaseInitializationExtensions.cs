using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventTicketing.ServiceDefaults.Persistence;

public static class DatabaseInitializationExtensions
{
    public static async Task EnsureDatabaseAsync<TContext>(
        this IHost host,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        const int maximumAttempts = 10;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var logger = scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(TContext).Name);

            try
            {
                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                await context.Database.EnsureCreatedAsync(cancellationToken);
                logger.LogInformation("Database for {DbContext} is ready", typeof(TContext).Name);
                return;
            }
            catch (Exception exception) when (attempt < maximumAttempts)
            {
                logger.LogWarning(exception,
                    "Database initialization attempt {Attempt}/{MaximumAttempts} failed",
                    attempt, maximumAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }
}
