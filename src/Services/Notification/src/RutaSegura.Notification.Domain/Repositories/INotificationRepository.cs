using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Domain.Repositories;

public interface INotificationRepository
{
    Task<NotificationEntity?> GetAsync(Guid organizationId, Guid notificationId, CancellationToken cancellationToken);
    Task<NotificationEntity?> FindByKeyAsync(NotificationKey key, CancellationToken cancellationToken);
    /// <summary>Filtra por organizacion, propietario y vinculos vigentes ANTES de paginar. Incluye avisos sin StudentId.</summary>
    Task<IReadOnlyList<NotificationEntity>> ListAsync(InboxFilter filter, CancellationToken cancellationToken);
    void Add(NotificationEntity notification);
}
