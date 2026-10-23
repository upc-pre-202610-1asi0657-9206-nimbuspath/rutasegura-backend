using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RutaSegura.BuildingBlocks.Messaging;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Infrastructure.LiveUpdates;

namespace RutaSegura.TripTracking.Infrastructure.Messaging.RabbitMq;

internal static class AmqpJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// Adaptador RabbitMQ de <see cref="IMessagePublisher"/> (BuildingBlocks.Messaging) para el exchange de eventos.
/// Usa publisher confirms: <c>PublishAsync</c> termina cuando el broker aceptó y persistió el mensaje
/// o lanza excepción; así el Outbox solo marca como publicado lo que realmente llegó.
/// </summary>
internal sealed class RabbitMqMessagePublisher(RabbitMqConnection connection, ILogger<RabbitMqMessagePublisher> logger)
    : IMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync<TPayload>(IntegrationMessage<TPayload> message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var body = JsonSerializer.SerializeToUtf8Bytes(message.Payload, AmqpJson.Options);

        var properties = new BasicProperties
        {
            MessageId = message.MessageId.ToString(),
            Type = message.MessageType,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            DeliveryMode = DeliveryModes.Persistent,
            CorrelationId = message.CorrelationId,
            AppId = connection.Options.ClientName,
            Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                [MessageHeaders.MessageType] = message.MessageType,
                [MessageHeaders.SchemaVersion] = message.SchemaVersion,
                [MessageHeaders.OccurredAt] = message.OccurredAt.ToString("O"),
                [MessageHeaders.CorrelationId] = message.CorrelationId
            }
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            await channel.BasicPublishAsync(connection.Options.EventsExchange, routingKey: message.MessageType,
                mandatory: true, basicProperties: properties, body: body, cancellationToken: cancellationToken);
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
        await RabbitMqTopology.DeclareEventsExchangeAsync(channel, connection.Options.EventsExchange, cancellationToken);
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
            logger.LogDebug(ex, "Error al descartar el canal de publicación");
        }

        _channel = null;
    }

    public async ValueTask DisposeAsync()
    {
        await ResetChannelAsync();
        _gate.Dispose();
    }
}

/// <summary>
/// Difusión de telemetría en vivo por el exchange topic <c>trip-tracking.live</c>
/// (routing keys <c>position.{tripId}</c> y <c>status.{tripId}</c>). Mensajes no persistentes con TTL corto:
/// una posición vieja no sirve, la reemplaza el siguiente ping.
/// </summary>
internal sealed class RabbitMqLiveUpdateBroadcaster(RabbitMqConnection connection, ILogger<RabbitMqLiveUpdateBroadcaster> logger)
    : ILiveUpdateBroadcaster, IAsyncDisposable
{
    public const string PositionType = "trip-tracking.live-position";
    public const string StatusType = "trip-tracking.live-status";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public Task BroadcastPositionAsync(LivePositionUpdate update, CancellationToken cancellationToken) =>
        PublishAsync($"position.{update.TripId}", PositionType, update, cancellationToken);

    public Task BroadcastStatusAsync(LiveTripStatusUpdate update, CancellationToken cancellationToken) =>
        PublishAsync($"status.{update.TripId}", StatusType, update, cancellationToken);

    private async Task PublishAsync<T>(string routingKey, string type, T payload, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            Type = type,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Transient,
            Expiration = "15000"
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, AmqpJson.Options);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not { IsOpen: true })
            {
                var conn = await connection.GetAsync(cancellationToken);
                _channel = await conn.CreateChannelAsync(cancellationToken: cancellationToken);
                await RabbitMqTopology.DeclareLiveExchangeAsync(_channel, connection.Options.LiveExchange, cancellationToken);
            }

            await _channel.BasicPublishAsync(connection.Options.LiveExchange, routingKey, mandatory: false,
                basicProperties: properties, body: body, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo difundir la actualización en vivo {RoutingKey}", routingKey);
            if (_channel is not null)
            {
                await _channel.DisposeAsync();
                _channel = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        _gate.Dispose();
    }
}
