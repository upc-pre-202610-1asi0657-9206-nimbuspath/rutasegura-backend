using System.Globalization;
using RabbitMQ.Client;
using RutaSegura.Notification.Infrastructure.Messaging.Inbound;

namespace RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;

public sealed class NotificationFailurePublisher(NotificationRabbitMqOptions options, IChannel channel) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ForwardAsync(NotificationIncomingMessage message, int? attempt, string code, CancellationToken ct)
    {
        var headers = message.Headers is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(message.Headers);
        // No registrar el payload ni cabeceras de credenciales; solo codigos estables para operacion.
        headers["x-notification-error-code"] = code;
        headers[TripTrackingMessageDecoder.OriginalRoutingHeader] =
            message.RoutingKey == options.Queue
                ? headers.GetValueOrDefault(TripTrackingMessageDecoder.OriginalRoutingHeader) ?? message.MessageType
                : message.RoutingKey;
        if (attempt is not null) headers[TripTrackingMessageDecoder.RetryHeader] = attempt.Value;
        var properties = new BasicProperties
        {
            MessageId = message.MessageId, Type = message.MessageType, CorrelationId = message.CorrelationId,
            ContentType = message.ContentType, ContentEncoding = "utf-8", Headers = headers,
            Timestamp = new AmqpTimestamp(message.UnixTimestamp), DeliveryMode = DeliveryModes.Persistent,
            AppId = "notification-retry"
        };
        await _gate.WaitAsync(ct);
        try
        {
            await channel.BasicPublishAsync(attempt is null ? options.ErrorExchange : options.RetryExchange,
                attempt?.ToString(CultureInfo.InvariantCulture) ?? options.Queue,
                mandatory: true, basicProperties: properties, body: message.Body, cancellationToken: ct);
        }
        finally { _gate.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
