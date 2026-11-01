using RutaSegura.Notification.Domain.Model.Entities;

namespace RutaSegura.Notification.Domain.Repositories;

public interface INotificationDeliveryAdministrationRepository
{
    Task<IReadOnlyList<NotificationDelivery>> ListFailedAsync(Guid organization, int offset, int limit, CancellationToken ct);
    Task<NotificationDeliveryReplay?> FindReplayAsync(Guid organization, Guid requestId, CancellationToken ct);
    void Add(NotificationDeliveryReplay replay);
}
