using RutaSegura.Notification.Domain.Model.Entities;

namespace RutaSegura.Notification.Domain.Repositories;

public interface INotificationDispatchRepository
{
    Task<IReadOnlyList<NotificationDispatchJob>> ListPendingJobsAsync(int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationDelivery>> ListDueDeliveriesAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);
    void Add(NotificationDelivery delivery);
}
