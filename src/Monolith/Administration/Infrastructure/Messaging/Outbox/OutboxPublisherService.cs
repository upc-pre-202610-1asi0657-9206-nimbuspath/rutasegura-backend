using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;

namespace Monolith.Administration.Infrastructure.Messaging.Outbox;

/// <summary>
///     Relay of the Transactional Outbox: publishes committed messages to <c>administration.events</c>.
///     <list type="bullet">
///         <item>At-least-once: a message published but not yet marked is sent again with the same id; consumers dedupe.</item>
///         <item>Several monolith instances can run it: on MySQL each batch is taken with <c>FOR UPDATE SKIP LOCKED</c>.</item>
///         <item>Broker down: exponential backoff per message; nothing is lost.</item>
///     </list>
/// </summary>
public sealed class OutboxPublisherService(
    IServiceScopeFactory scopes,
    IOutboxMessagePublisher publisher,
    IOptions<AdministrationMessagingOptions> options,
    TimeProvider clock,
    ILogger<OutboxPublisherService> logger) : BackgroundService
{
    private DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var errorDelay = TimeSpan.FromSeconds(2);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatchAsync(stoppingToken);
                await CleanupAsync(stoppingToken);
                errorDelay = TimeSpan.FromSeconds(2);
                if (processed < options.Value.OutboxBatchSize)
                    await Task.Delay(options.Value.OutboxPollingInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Administration outbox: batch failed; retrying in {Delay}", errorDelay);
                await Task.Delay(errorDelay, clock, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
                errorDelay = TimeSpan.FromSeconds(Math.Min(errorDelay.TotalSeconds * 2, 60));
            }
        }
    }

    /// <summary>Publishes one batch. Returns how many messages were attempted.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();
        var batchSize = options.Value.OutboxBatchSize;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var batch = IsMySql(context)
            ? await context.Set<OutboxMessage>()
                .FromSqlRaw(
                    """
                    SELECT * FROM administration_outbox_messages
                    WHERE processed_at IS NULL AND (next_attempt_at IS NULL OR next_attempt_at <= {0})
                    ORDER BY occurred_at, created_at
                    LIMIT {1}
                    FOR UPDATE SKIP LOCKED
                    """, now.UtcDateTime, batchSize)
                .ToListAsync(cancellationToken)
            : await context.Set<OutboxMessage>()
                .Where(m => m.ProcessedAt == null && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
                .OrderBy(m => m.OccurredAt).ThenBy(m => m.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(message, cancellationToken);
                message.ProcessedAt = clock.GetUtcNow();
                message.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = Truncate($"{ex.GetType().Name}: {ex.Message}", 1000);
                message.NextAttemptAt = clock.GetUtcNow() + Backoff(message.Attempts);
                logger.LogWarning(ex, "Administration outbox: {MessageType} {MessageId} not published (attempt {Attempt})",
                    message.MessageType, message.Id, message.Attempts);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
    }

    private TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Math.Min(attempts, 16)),
            options.Value.OutboxMaxBackoff.TotalSeconds));

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (now < _nextCleanup) return;
        _nextCleanup = now.AddHours(1);

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var limit = now - options.Value.OutboxRetention;
        await context.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < limit)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static bool IsMySql(DbContext context) =>
        context.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
