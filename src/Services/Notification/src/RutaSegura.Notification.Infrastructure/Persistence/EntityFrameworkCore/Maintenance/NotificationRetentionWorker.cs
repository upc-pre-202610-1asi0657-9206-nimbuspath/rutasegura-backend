using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RutaSegura.Notification.Infrastructure.Channels;

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Maintenance;

public sealed class NotificationRetentionWorker(IServiceScopeFactory scopes, NotificationRetentionOptions options,
    MySqlDispatchLock locks, TimeProvider clock, ILogger<NotificationRetentionWorker> logger) : BackgroundService
{
    private static readonly Guid LockId = Guid.Parse("b4d63b25-645c-45c8-a4d7-a9c2f0020001");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("No se pudo completar la retencion de Notification; se conserva el trabajo para la siguiente ejecucion."); }
            try { await Task.Delay(options.Interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        if (!options.Enabled) return 0;
        options.Validate();
        await using var lease = await locks.TryAcquireAsync(LockId, ct);
        if (lease is null) return 0;
        var cutoff = clock.GetUtcNow().AddDays(-options.ReadNoticesDays);
        var count = 0;
        for (var batch = 0; batch < options.MaxBatchesPerRun; batch++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var removed = await scope.ServiceProvider.GetRequiredService<NotificationRetentionStore>().ExecuteBatchAsync(cutoff, options.BatchSize, options.DryRun, ct);
            count += removed;
            if (options.DryRun || removed < options.BatchSize) break;
        }
        logger.LogInformation("Retencion de Notification: {Mode}, {Count} avisos candidatos/procesados.", options.DryRun ? "preview" : "delete", count);
        return count;
    }
}
