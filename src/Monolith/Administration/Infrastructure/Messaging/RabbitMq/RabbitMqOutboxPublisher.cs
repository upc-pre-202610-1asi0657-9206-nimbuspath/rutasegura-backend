using System.Text;
using Monolith.Administration.Infrastructure.Messaging.Outbox;
using RabbitMQ.Client;

namespace Monolith.Administration.Infrastructure.Messaging.RabbitMq;

/// <summary>
///     Publishes outbox messages to <c>administration.events</c> with publisher confirms: <c>PublishAsync</c> returns
///     only when the broker accepted and persisted the message, so the outbox never marks a lost message as sent.
///     Routing key = message type (e.g. <c>administration.route-published</c>); MessageId = event id.
/// </summary>
public sealed class RabbitMqOutboxPublisher(RabbitMqConnection connection, ILogger<RabbitMqOutboxPublisher> logger)
    : IOutboxMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var properties = new BasicProperties
        {
            MessageId = message.Id.ToString(),
            Type = message.MessageType,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            DeliveryMode = DeliveryModes.Persistent,
            CorrelationId = message.CorrelationId,
            AppId = connection.Options.ClientName,
            Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                ["message-type"] = message.MessageType,
                ["schema-version"] = message.SchemaVersion,
                ["occurred-at"] = message.OccurredAt.ToString("O"),
                ["correlation-id"] = message.CorrelationId
            }
        };
        var body = Encoding.UTF8.GetBytes(message.Payload);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            await channel.BasicPublishAsync(connection.Options.EventsExchange, routingKey: message.MessageType,
                mandatory: false, basicProperties: properties, body: body, cancellationToken: cancellationToken);
        }
        catch
        {
            await ResetChannelAsync();
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
        await ResetChannelAsync();

        var conn = await connection.GetAsync(cancellationToken);
        var channel = await conn.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await RabbitMqTopology.DeclareEventsExchangeAsync(channel, connection.Options.EventsExchange,
            cancellationToken);
        _channel = channel;
        return channel;
    }

    private async Task ResetChannelAsync()
    {
        if (_channel is null) return;
        try
        {
            await _channel.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error disposing the publishing channel");
        }

        _channel = null;
    }

    public async ValueTask DisposeAsync()
    {
        await ResetChannelAsync();
        _gate.Dispose();
    }
}
