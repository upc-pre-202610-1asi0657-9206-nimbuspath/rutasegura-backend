using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;

namespace RutaSegura.Notification.Infrastructure.Channels;

public sealed class NotificationBacklogMonitor(IServiceScopeFactory scopes, TimeProvider clock, ILogger<NotificationBacklogMonitor> logger) : BackgroundService
{
    public const string MeterName = "RutaSegura.Notification.Backlog";
    private readonly Meter _meter = new(MeterName, "1.0.0");
    private long _jobs, _deliveries, _failures;
    private double _oldestAge;
    private int _available;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _meter.CreateObservableGauge("notification.dispatch.pending", () => new[]
        {
            new Measurement<long>(Interlocked.Read(ref _jobs), new KeyValuePair<string, object?>("kind", "job")),
            new Measurement<long>(Interlocked.Read(ref _deliveries), new KeyValuePair<string, object?>("kind", "delivery"))
        });
        _meter.CreateObservableGauge("notification.dispatch.failed", () => Interlocked.Read(ref _failures));
        _meter.CreateObservableGauge("notification.dispatch.oldest.age", () => Volatile.Read(ref _oldestAge), "s");
        _meter.CreateObservableGauge("notification.backlog.sample.available", () => Volatile.Read(ref _available));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                Interlocked.Exchange(ref _jobs, await db.DispatchJobs.LongCountAsync(j => j.ExpandedAt == null, stoppingToken));
                Interlocked.Exchange(ref _deliveries, await db.Deliveries.LongCountAsync(d => d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.RetryScheduled, stoppingToken));
                Interlocked.Exchange(ref _failures, await db.Deliveries.LongCountAsync(d => d.Status == DeliveryStatus.Failed, stoppingToken));
                var oldest = await db.Deliveries.Where(d => d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.RetryScheduled)
                    .OrderBy(d => d.CreatedAt).Select(d => (DateTimeOffset?)d.CreatedAt).FirstOrDefaultAsync(stoppingToken);
                var oldestJob = await db.DispatchJobs.Where(j => j.ExpandedAt == null).OrderBy(j => j.CreatedAt)
                    .Select(j => (DateTimeOffset?)j.CreatedAt).FirstOrDefaultAsync(stoppingToken);
                if (oldestJob is not null && (oldest is null || oldestJob < oldest)) oldest = oldestJob;
                Volatile.Write(ref _oldestAge, oldest is null ? 0 : Math.Max(0, (clock.GetUtcNow() - oldest.Value).TotalSeconds));
                Volatile.Write(ref _available, 1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                Volatile.Write(ref _available, 0); // No interpretar una muestra anterior como estado actual.
                logger.LogWarning("No se pudo actualizar la muestra del backlog de Notification.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public override void Dispose() { _meter.Dispose(); base.Dispose(); }
}
