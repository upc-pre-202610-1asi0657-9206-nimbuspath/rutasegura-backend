using Microsoft.EntityFrameworkCore;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories;

public sealed class NotificationDispatchRepository(NotificationDbContext context) : INotificationDispatchQueue, INotificationDispatchRepository
{
    public void Enqueue(NotificationEntity notification)
    {
        if (!context.DispatchJobs.Local.Any(j => j.OrganizationId == notification.OrganizationId && j.NotificationId == notification.Id))
            context.DispatchJobs.Add(NotificationDispatchJob.Schedule(notification));
    }
    public async Task<IReadOnlyList<NotificationDispatchJob>> ListPendingJobsAsync(int limit, CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        return await context.DispatchJobs.Where(j => j.ExpandedAt == null)
            .OrderBy(j => j.CreatedAt).ThenBy(j => j.Id).Take(limit).ToArrayAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<NotificationDelivery>> ListDueDeliveriesAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        return await context.Deliveries.Where(d => d.Status == DeliveryStatus.Pending ||
                (d.Status == DeliveryStatus.RetryScheduled && d.NextAttemptAt <= now))
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).Take(limit).ToArrayAsync(cancellationToken);
    }
    public void Add(NotificationDelivery delivery) => context.Deliveries.Add(delivery);
    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
    }
}
