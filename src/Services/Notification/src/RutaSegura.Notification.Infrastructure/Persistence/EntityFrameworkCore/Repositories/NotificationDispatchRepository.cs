using Microsoft.EntityFrameworkCore;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories;

public sealed class NotificationDispatchRepository(NotificationDbContext context) : INotificationDispatchQueue, INotificationDispatchRepository, INotificationDeliveryAdministrationRepository
{
    public async Task<IReadOnlyList<NotificationDelivery>> ListFailedAsync(Guid organization, int offset, int limit, CancellationToken ct)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return await context.Deliveries.AsNoTracking().Where(d => d.OrganizationId == organization && d.Status == DeliveryStatus.Failed)
            .OrderByDescending(d => d.LastAttemptAt).ThenBy(d => d.Id).Skip(offset).Take(limit).ToArrayAsync(ct);
    }
    public Task<NotificationDeliveryReplay?> FindReplayAsync(Guid organization, Guid requestId, CancellationToken ct) =>
        context.DeliveryReplays.SingleOrDefaultAsync(r => r.OrganizationId == organization && r.RequestId == requestId, ct);
    public void Add(NotificationDeliveryReplay replay) => context.DeliveryReplays.Add(replay);
    public Task<NotificationDispatchJob?> GetJobAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) =>
        context.DispatchJobs.SingleOrDefaultAsync(j => j.OrganizationId == organizationId && j.Id == jobId, cancellationToken);
    public Task<NotificationDelivery?> GetDeliveryAsync(Guid organizationId, Guid deliveryId, CancellationToken cancellationToken) =>
        context.Deliveries.SingleOrDefaultAsync(d => d.OrganizationId == organizationId && d.Id == deliveryId, cancellationToken);
    public void Enqueue(NotificationEntity notification)
    {
        if (!context.DispatchJobs.Local.Any(j => j.OrganizationId == notification.OrganizationId && j.NotificationId == notification.Id))
            context.DispatchJobs.Add(NotificationDispatchJob.Schedule(notification));
    }
    public async Task<IReadOnlyList<NotificationDispatchJob>> ListPendingJobsAsync(int limit, CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        return await context.DispatchJobs.Where(j => j.ExpandedAt == null)
            .Join(context.Notifications, j => new { j.OrganizationId, Id = j.NotificationId }, n => new { n.OrganizationId, n.Id },
                (job, notice) => new { Job = job, notice.Category })
            .OrderBy(x => x.Category == NotificationCategory.Sos ? 0 : x.Category == NotificationCategory.Incident ? 1 : 2)
            .ThenBy(x => x.Job.CreatedAt).ThenBy(x => x.Job.Id).Select(x => x.Job).Take(limit).ToArrayAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<NotificationDelivery>> ListDueDeliveriesAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        return await context.Deliveries.Where(d => d.Status == DeliveryStatus.Pending ||
                (d.Status == DeliveryStatus.RetryScheduled && d.NextAttemptAt <= now))
            .Join(context.Notifications, d => new { d.OrganizationId, Id = d.NotificationId }, n => new { n.OrganizationId, n.Id },
                (delivery, notice) => new { Delivery = delivery, notice.Category })
            .OrderBy(x => x.Category == NotificationCategory.Sos ? 0 : x.Category == NotificationCategory.Incident ? 1 : 2)
            .ThenBy(x => x.Delivery.CreatedAt).ThenBy(x => x.Delivery.Id).Select(x => x.Delivery).Take(limit).ToArrayAsync(cancellationToken);
    }
    public void Add(NotificationDelivery delivery) => context.Deliveries.Add(delivery);
    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
    }
}
