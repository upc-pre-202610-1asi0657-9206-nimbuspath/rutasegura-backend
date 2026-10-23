using Microsoft.Extensions.Logging;
using RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;

namespace RutaSegura.Notification.Infrastructure.Messaging.Inbound;

/// <summary>ACK o transferencia confirmada. Los metodos no pueden fingir exito ante un fallo AMQP.</summary>
public interface INotificationDeliveryActions
{
    Task AcknowledgeAsync(CancellationToken cancellationToken);
    Task ForwardAsync(int? retryAttempt, string errorCode, CancellationToken cancellationToken);
}

public sealed class NotificationDeliveryCoordinator(INotificationMessageProcessor processor, NotificationRabbitMqOptions options,
    ILogger<NotificationDeliveryCoordinator> logger)
{
    public async Task HandleAsync(NotificationIncomingMessage message, INotificationDeliveryActions actions, CancellationToken ct)
    {
        int attempt;
        try { attempt = TripTrackingMessageDecoder.RetryCount(message); }
        catch (Exception ex) when (ex is InvalidNotificationMessageException or System.Text.DecoderFallbackException)
        {
            await actions.ForwardAsync(null, "NOTIFICATION_INVALID_RETRY_HEADER", ct);
            await actions.AcknowledgeAsync(ct);
            return;
        }
        var result = await processor.ProcessAsync(message, ct);
        if (result.Outcome == NotificationConsumeOutcome.Committed)
        {
            // Handle del comando ya espero SaveChanges/commit (o comprobo un Inbox previamente confirmado).
            await actions.AcknowledgeAsync(ct);
            return;
        }
        int? nextAttempt = result.Outcome == NotificationConsumeOutcome.Retry && attempt < options.RetryDelaysMilliseconds.Length
            ? attempt + 1 : null;
        var code = result.Outcome == NotificationConsumeOutcome.Retry && nextAttempt is null
            ? "NOTIFICATION_RETRIES_EXHAUSTED" : result.Code;
        await actions.ForwardAsync(nextAttempt, code, ct); // publisher confirm + mandatory antes de soltar el original
        await actions.AcknowledgeAsync(ct);
        logger.LogWarning("Mensaje {MessageId}: {Outcome}, intento {Attempt}, codigo {Code}",
            message.MessageId, nextAttempt is null ? "DLQ" : "retry", nextAttempt ?? attempt, code);
    }
}
