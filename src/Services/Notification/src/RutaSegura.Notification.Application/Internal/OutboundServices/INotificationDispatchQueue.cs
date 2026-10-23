using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Application.Internal.OutboundServices;

/// <summary>Inserta trabajo durable en LA MISMA unidad de trabajo que Notification (MySQL en fase 2).
/// No llama a FCM, SignalR ni RabbitMQ dentro de la transaccion.</summary>
public interface INotificationDispatchQueue
{
    void Enqueue(NotificationEntity notification);
}
