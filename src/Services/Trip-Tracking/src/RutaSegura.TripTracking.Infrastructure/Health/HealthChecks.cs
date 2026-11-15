using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RutaSegura.TripTracking.Infrastructure.Messaging.RabbitMq;
using RutaSegura.TripTracking.Infrastructure.Persistence;

namespace RutaSegura.TripTracking.Infrastructure.Health;

// Readiness (/health/ready): el orquestador (ECS / docker-compose) solo envía tráfico si las dependencias responden.

internal sealed class MySqlHealthCheck(TripTrackingDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("MySQL responde")
                : HealthCheckResult.Unhealthy("MySQL no responde");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MySQL no responde", ex);
        }
    }
}

internal sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var conn = await connection.GetAsync(cancellationToken);
            return conn.IsOpen ? HealthCheckResult.Healthy("RabbitMQ conectado") : HealthCheckResult.Unhealthy("Conexión cerrada");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ no disponible", ex);
        }
    }
}

/// <summary>Redis es caché: si falla, el servicio sigue (cache-aside), por eso reporta Degraded y no Unhealthy.</summary>
internal sealed class CacheHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.GetStringAsync("trip-tracking:health", cancellationToken);
            return HealthCheckResult.Healthy("Caché disponible");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Caché no disponible: se usa MySQL", ex);
        }
    }
}
