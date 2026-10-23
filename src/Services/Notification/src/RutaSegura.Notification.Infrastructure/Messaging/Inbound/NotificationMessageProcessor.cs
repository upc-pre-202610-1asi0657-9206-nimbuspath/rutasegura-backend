using Microsoft.Extensions.DependencyInjection;
using RutaSegura.Notification.Application.CommandServices;

namespace RutaSegura.Notification.Infrastructure.Messaging.Inbound;

public enum NotificationConsumeOutcome { Committed, Retry, Rejected }
public sealed record NotificationConsumeResult(NotificationConsumeOutcome Outcome, string Code);

public interface INotificationMessageProcessor
{
    Task<NotificationConsumeResult> ProcessAsync(NotificationIncomingMessage message, CancellationToken cancellationToken);
}

public sealed class NotificationMessageProcessor(IServiceScopeFactory scopes, TripTrackingMessageDecoder decoder) : INotificationMessageProcessor
{
    public async Task<NotificationConsumeResult> ProcessAsync(NotificationIncomingMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var command = decoder.Decode(message);
            // Nuevo DbContext por intento. Nunca se reutiliza el ChangeTracker que fallo un commit anterior.
            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<INotificationCommandService>();
            var result = await service.Handle(command, cancellationToken);
            return result.IsSuccess
                ? new(NotificationConsumeOutcome.Committed, "NOTIFICATION_COMMITTED")
                : new(NotificationConsumeOutcome.Rejected, result.FirstError.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (InvalidNotificationMessageException ex) { return new(NotificationConsumeOutcome.Rejected, ex.Code); }
        catch (Exception)
        {
            // Incluye timeouts y carreras de unicidad. No dar ACK por una excepcion de duplicado:
            // el siguiente intento consulta el Inbox confirmado en un scope limpio.
            return new(NotificationConsumeOutcome.Retry, "NOTIFICATION_PROCESSING_UNAVAILABLE");
        }
    }
}
