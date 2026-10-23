using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RutaSegura.TripTracking.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    /// <summary>
    /// Aplica las migraciones pendientes reintentando mientras MySQL arranca (docker-compose).
    /// Se usa con <c>Persistence:MigrateOnStartup=true</c> o con el argumento <c>--migrate</c>.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("TripTracking.Migrations");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TripTrackingDbContext>();
                var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pending.Count > 0)
                {
                    logger.LogInformation("Aplicando {Count} migración(es): {Migrations}", pending.Count, string.Join(", ", pending));
                    await db.Database.MigrateAsync(cancellationToken);
                }

                logger.LogInformation("Base de datos de Trip & Tracking al día");
                return;
            }
            catch (Exception ex) when (attempt < 20 && ex is not OperationCanceledException)
            {
                logger.LogWarning("MySQL aún no disponible (intento {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }
}

/// <summary>Fábrica para las herramientas de EF (<c>dotnet ef migrations add ...</c>) sin arrancar la API.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TripTrackingDbContext>
{
    public TripTrackingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__TripTracking")
            ?? "server=localhost;port=3306;database=rutasegura_trip_tracking;user=rutasegura;password=rutasegura";
        var options = new DbContextOptionsBuilder<TripTrackingDbContext>()
            .UseMySQL(connectionString, mysql => mysql.MigrationsHistoryTable("__ef_migrations_history"))
            .Options;
        return new TripTrackingDbContext(options);
    }
}
