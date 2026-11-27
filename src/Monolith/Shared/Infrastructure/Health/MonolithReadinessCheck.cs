using Microsoft.Extensions.Diagnostics.HealthChecks;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;

namespace Monolith.Shared.Infrastructure.Health;

public sealed class MonolithStartupState { public bool MigrationsCompleted { get; set; } }

public sealed class MonolithReadinessCheck(AppDbContext database, MonolithStartupState startup) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!startup.MigrationsCompleted) return HealthCheckResult.Unhealthy("Database initialization is incomplete.");
        try
        {
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Database unavailable.");
        }
        catch (Exception) { return HealthCheckResult.Unhealthy("Database unavailable."); }
    }
}
