using RutaSegura.Notification.Domain.Model.Entities;

namespace RutaSegura.Notification.Domain.Repositories;

public interface INotificationDispatchRepository
{
    Task<NotificationDispatchJob?> GetJobAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);
    Task<NotificationDelivery?> GetDeliveryAsync(Guid organizationId, Guid deliveryId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationDispatchJob>> ListPendingJobsAsync(int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationDelivery>> ListDueDeliveriesAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);
    void Add(NotificationDelivery delivery);
}
