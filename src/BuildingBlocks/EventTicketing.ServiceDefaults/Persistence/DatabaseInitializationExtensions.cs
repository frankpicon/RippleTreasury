using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventTicketing.ServiceDefaults.Persistence;

public static class DatabaseInitializationExtensions
{
    public static async Task<bool> InitializeDatabaseAsync<TContext>(
        this IHost host,
        string[] args,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        const int maximumAttempts = 10;
        var migrateOnly = args.Contains("--migrate", StringComparer.Ordinal);
        var environment = host.Services.GetRequiredService<IHostEnvironment>();
        var applyMigrations = migrateOnly || environment.IsDevelopment() || environment.IsEnvironment("Testing");

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var logger = scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(TContext).Name);

            try
            {
                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                if (applyMigrations)
                    await context.Database.MigrateAsync(cancellationToken);
                else if ((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                    throw new InvalidOperationException(
                        "Database migrations are pending. Run this service with --migrate as a deployment step.");
                logger.LogInformation("Database for {DbContext} is ready", typeof(TContext).Name);
                return migrateOnly;
            }
            catch (Exception exception) when (attempt < maximumAttempts &&
                                              exception is not InvalidOperationException &&
                                              exception is not OperationCanceledException)
            {
                logger.LogWarning(exception,
                    "Database initialization attempt {Attempt}/{MaximumAttempts} failed",
                    attempt, maximumAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new InvalidOperationException("Database initialization did not complete.");
    }
}
