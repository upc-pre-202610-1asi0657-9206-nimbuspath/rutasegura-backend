using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace RutaSegura.Notification.Api.Health;

/// <summary>Conexion de monitoreo reutilizable; no reemplaza ni comparte el canal Redis del dominio de Tracking.</summary>
public sealed class NotificationRedisReadinessCheck(IConfiguration configuration) : IHealthCheck, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ConnectionMultiplexer? _redis;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_redis is null)
                {
                    var options = ConfigurationOptions.Parse(configuration["NotificationRealtime:RedisConnection"]!);
                    options.AbortOnConnectFail = false;
                    options.ConnectRetry = 0;
                    options.ConnectTimeout = options.AsyncTimeout = options.SyncTimeout = 1000;
                    _redis = await ConnectionMultiplexer.ConnectAsync(options);
                }
            }
            finally { _gate.Release(); }
            await _redis.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception) { return HealthCheckResult.Unhealthy("Backplane no disponible."); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_redis is not null) await _redis.DisposeAsync();
        _gate.Dispose();
    }
}
