using RutaSegura.Notification.Domain.Model.Entities;

namespace RutaSegura.Notification.Domain.Repositories;

public interface IProcessedNotificationEventRepository
{
    Task<ProcessedNotificationEvent?> FindAsync(string consumer, Guid messageId, CancellationToken cancellationToken);
    void Add(ProcessedNotificationEvent processedEvent);
}
