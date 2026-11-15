using Microsoft.EntityFrameworkCore;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories;

public sealed class ProcessedNotificationEventRepository(NotificationDbContext context) : IProcessedNotificationEventRepository
{
    public Task<ProcessedNotificationEvent?> FindAsync(string consumer, Guid messageId, CancellationToken cancellationToken) =>
        context.ProcessedEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Consumer == consumer && e.MessageId == messageId, cancellationToken);
    public void Add(ProcessedNotificationEvent processedEvent) => context.ProcessedEvents.Add(processedEvent);
}
