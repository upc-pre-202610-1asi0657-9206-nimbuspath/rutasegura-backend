using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using RabbitMQ.Client;

namespace Monolith.Stakeholder.Infrastructure.Messaging;

/// <summary>Sends one outbox message to the broker; completes only when the broker confirmed it.</summary>
public interface IStakeholderMessagePublisher
{
    Task PublishAsync(StakeholderOutboxMessage message, CancellationToken cancellationToken);
}

/// <summary>
///     Relay of the Stakeholder outbox: publishes committed messages to <c>stakeholder.events</c> at least once
///     (consumers dedupe by MessageId), with exponential backoff while the broker is down. On MySQL each batch is
///     taken with <c>FOR UPDATE SKIP LOCKED</c>, so several monolith instances can run it.
/// </summary>
public sealed class StakeholderOutboxPublisherService(
    IServiceScopeFactory scopes,
    IStakeholderMessagePublisher publisher,
    IOptions<StakeholderMessagingOptions> options,
    TimeProvider clock,
    ILogger<StakeholderOutboxPublisherService> logger) : BackgroundService
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
                logger.LogWarning(ex, "Stakeholder outbox: batch failed; retrying in {Delay}", errorDelay);
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
        var batch = context.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true
            ? await context.Set<StakeholderOutboxMessage>()
                .FromSqlRaw(
                    """
                    SELECT * FROM stakeholder_outbox_messages
                    WHERE processed_at IS NULL AND (next_attempt_at IS NULL OR next_attempt_at <= {0})
                    ORDER BY occurred_at, created_at
                    LIMIT {1}
                    FOR UPDATE SKIP LOCKED
                    """, now.UtcDateTime, batchSize)
                .ToListAsync(cancellationToken)
            : await context.Set<StakeholderOutboxMessage>()
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
                var error = $"{ex.GetType().Name}: {ex.Message}";
                message.LastError = error.Length <= 1000 ? error : error[..1000];
                message.NextAttemptAt = clock.GetUtcNow() + TimeSpan.FromSeconds(Math.Min(
                    Math.Pow(2, Math.Min(message.Attempts, 16)), options.Value.OutboxMaxBackoff.TotalSeconds));
                logger.LogWarning(ex, "Stakeholder outbox: {MessageType} {MessageId} not published (attempt {Attempt})",
                    message.MessageType, message.Id, message.Attempts);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (now < _nextCleanup) return;
        _nextCleanup = now.AddHours(1);

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var limit = now - options.Value.OutboxRetention;
        await context.Set<StakeholderOutboxMessage>()
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < limit)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

/// <summary>
///     Publishes to <c>stakeholder.events</c> with publisher confirms (routing key = message type, MessageId = event
///     id). The exchange has an alternate exchange so nothing is lost while no consumer is bound.
/// </summary>
public sealed class RabbitMqStakeholderPublisher(
    IOptions<StakeholderMessagingOptions> options,
    ILogger<RabbitMqStakeholderPublisher> logger) : IStakeholderMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(StakeholderOutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var o = options.Value;
        var properties = new BasicProperties
        {
            MessageId = message.Id.ToString(),
            Type = message.MessageType,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            DeliveryMode = DeliveryModes.Persistent,
            CorrelationId = message.CorrelationId,
            AppId = o.ClientName,
            Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                ["message-type"] = message.MessageType,
                ["schema-version"] = message.SchemaVersion,
                ["occurred-at"] = message.OccurredAt.ToString("O"),
                ["correlation-id"] = message.CorrelationId
            }
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            await channel.BasicPublishAsync(o.EventsExchange, routingKey: message.MessageType, mandatory: false,
                basicProperties: properties, body: Encoding.UTF8.GetBytes(message.Payload),
                cancellationToken: cancellationToken);
        }
        catch
        {
            await ResetAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true }) return _channel;
        await ResetAsync();

        var o = options.Value;
        if (_connection is not { IsOpen: true })
        {
            _connection = await new ConnectionFactory
            {
                HostName = o.HostName,
                Port = o.Port,
                UserName = o.UserName,
                Password = o.Password,
                VirtualHost = o.VirtualHost,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                RequestedHeartbeat = TimeSpan.FromSeconds(30),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                ClientProvidedName = o.ClientName
            }.CreateConnectionAsync(cancellationToken);
            logger.LogInformation("Stakeholder connected to RabbitMQ {Host}:{Port}{VirtualHost}", o.HostName, o.Port,
                o.VirtualHost);
        }

        var channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        var unrouted = o.EventsExchange + ".unrouted";
        await channel.ExchangeDeclareAsync(unrouted, ExchangeType.Fanout, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(unrouted, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(unrouted, unrouted, routingKey: string.Empty, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(o.EventsExchange, ExchangeType.Topic, durable: true, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["alternate-exchange"] = unrouted },
            cancellationToken: cancellationToken);
        _channel = channel;
        return channel;
    }

    private async Task ResetAsync()
    {
        if (_channel is null) return;
        try
        {
            await _channel.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error disposing the Stakeholder publishing channel");
        }

        _channel = null;
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Error closing the Stakeholder RabbitMQ connection");
            }

            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}
