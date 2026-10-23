using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RutaSegura.BuildingBlocks.Messaging;
using RutaSegura.TripTracking.Infrastructure.Options;
using RutaSegura.TripTracking.Infrastructure.Persistence;
using RutaSegura.TripTracking.Infrastructure.Persistence.Records;

namespace RutaSegura.TripTracking.Infrastructure.Messaging.Outbox;

/// <summary>
/// Relay del Transactional Outbox (PT-04). Publica en RabbitMQ los eventos ya confirmados en MySQL.
/// <list type="bullet">
/// <item>Varias réplicas pueden correr a la vez: cada lote se toma con <c>FOR UPDATE SKIP LOCKED</c>.</item>
/// <item>Entrega al-menos-una-vez: si se cae después de publicar y antes de marcar, el mensaje se reenvía con
/// el mismo MessageId y los consumidores lo descartan por idempotencia.</item>
/// <item>Si el broker no está, reintenta con backoff exponencial; el evento nunca se pierde.</item>
/// </list>
/// </summary>
internal sealed class OutboxProcessor(
    IServiceScopeFactory scopes,
    IMessagePublisher publisher,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox iniciado (lote {Batch}, cada {Interval})", options.Value.BatchSize, options.Value.PollingInterval);
        var idleDelay = options.Value.PollingInterval;
        var errorDelay = TimeSpan.FromSeconds(2);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatchAsync(stoppingToken);
                await CleanupAsync(stoppingToken);
                errorDelay = TimeSpan.FromSeconds(2);
                if (processed < options.Value.BatchSize)
                    await Task.Delay(idleDelay, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox: error al procesar el lote; nuevo intento en {Delay}", errorDelay);
                await Task.Delay(errorDelay, clock, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
                errorDelay = TimeSpan.FromSeconds(Math.Min(errorDelay.TotalSeconds * 2, 60));
            }
        }
    }

    /// <summary>Procesa un lote. Devuelve cuántos mensajes intentó (publicados o no).</summary>
    internal async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripTrackingDbContext>();
        var now = clock.GetUtcNow();
        var nowUtc = now.UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var batch = await db.OutboxMessages
            .FromSqlRaw(
                """
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL AND (next_attempt_at IS NULL OR next_attempt_at <= {0})
                ORDER BY occurred_at, created_at
                LIMIT {1}
                FOR UPDATE SKIP LOCKED
                """, nowUtc, options.Value.BatchSize)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        var published = 0;
        foreach (var message in batch)
        {
            try
            {
                using var payload = JsonDocument.Parse(message.Payload);
                var envelope = new IntegrationMessage<JsonElement>(message.Id, message.MessageType, message.SchemaVersion,
                    message.OccurredAt, message.CorrelationId, payload.RootElement.Clone());
                await publisher.PublishAsync(envelope, cancellationToken);

                message.ProcessedAt = clock.GetUtcNow();
                message.LastError = null;
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = Truncate($"{ex.GetType().Name}: {ex.Message}", 1000);
                message.NextAttemptAt = clock.GetUtcNow() + Backoff(message.Attempts);
                logger.LogWarning(ex, "Outbox: no se pudo publicar {MessageType} {MessageId} (intento {Attempt})",
                    message.MessageType, message.Id, message.Attempts);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (published > 0) logger.LogDebug("Outbox: {Published}/{Total} mensajes publicados", published, batch.Count);
        return batch.Count;
    }

    private TimeSpan Backoff(int attempts)
    {
        var seconds = Math.Pow(2, Math.Min(attempts, 16));
        return TimeSpan.FromSeconds(Math.Min(seconds, options.Value.MaxBackoff.TotalSeconds));
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (now < _nextCleanup) return;
        _nextCleanup = now.AddHours(1);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripTrackingDbContext>();
        var limit = now - options.Value.Retention;
        var deleted = await db.OutboxMessages
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < limit)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0) logger.LogInformation("Outbox: {Deleted} mensajes antiguos eliminados", deleted);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
