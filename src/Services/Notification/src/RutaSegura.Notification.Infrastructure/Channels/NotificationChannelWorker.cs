using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Domain.Repositories;

namespace RutaSegura.Notification.Infrastructure.Channels;

public sealed class NotificationChannelWorker(IServiceScopeFactory scopes, MySqlDispatchLock locks,
    NotificationChannelOptions options, TimeProvider clock, ILogger<NotificationChannelWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Worker de canales no disponible ({Failure}).", ex.GetType().Name); }
            try { await Task.Delay(options.PollingInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        (Guid Org, Guid Id)[] jobs;
        (Guid Org, Guid Id)[] deliveries;
        await using (var selection = scopes.CreateAsyncScope())
        {
            var repository = selection.ServiceProvider.GetRequiredService<INotificationDispatchRepository>();
            jobs = (await repository.ListPendingJobsAsync(options.BatchSize, cancellationToken)).Select(j => (j.OrganizationId, j.Id)).ToArray();
        }
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = options.MaxConcurrency, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(jobs, parallel, async (job, ct) => await Process(job.Org, job.Id, true, ct));
        await using (var selection = scopes.CreateAsyncScope())
            deliveries = (await selection.ServiceProvider.GetRequiredService<INotificationDispatchRepository>()
                .ListDueDeliveriesAsync(clock.GetUtcNow(), options.BatchSize, cancellationToken)).Select(d => (d.OrganizationId, d.Id)).ToArray();
        await Parallel.ForEachAsync(deliveries, parallel, async (delivery, ct) => await Process(delivery.Org, delivery.Id, false, ct));
    }

    private async Task Process(Guid organization, Guid id, bool expansion, CancellationToken ct)
    {
        try
        {
            await using var held = await locks.TryAcquireAsync(id, ct);
            if (held is null) return;
            // Siempre recargar el estado dentro del lock; nunca reutilizar los objetos de la seleccion.
            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<INotificationChannelService>();
            if (expansion) await service.ExpandAsync(organization, id, ct);
            else await service.DeliverAsync(organization, id, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning("Trabajo {WorkId} pendiente ({Failure}); se reintentara en scope nuevo.", id, ex.GetType().Name); }
    }
}
