using System.Data;
using Microsoft.EntityFrameworkCore;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Maintenance;

public sealed class NotificationRetentionStore(NotificationDbContext db)
{
    public async Task<int> ExecuteBatchAsync(DateTimeOffset cutoff, int limit, bool dryRun, CancellationToken ct)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var candidates = await db.Notifications.Where(n => n.ReadAt != null && n.ReadAt < cutoff &&
            !db.DispatchJobs.Any(j => j.OrganizationId == n.OrganizationId && j.NotificationId == n.Id && j.ExpandedAt == null) &&
            !db.Deliveries.Any(d => d.OrganizationId == n.OrganizationId && d.NotificationId == n.Id &&
                (d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.RetryScheduled || d.Status == DeliveryStatus.Failed || d.ReplayCount > 0)))
            .OrderBy(n => n.ReadAt).ThenBy(n => n.Id).Select(n => n.Id).Take(limit).ToArrayAsync(ct);
        if (!dryRun && candidates.Length > 0)
        {
            await db.Deliveries.Where(d => candidates.Contains(d.NotificationId)).ExecuteDeleteAsync(ct);
            await db.DispatchJobs.Where(j => candidates.Contains(j.NotificationId)).ExecuteDeleteAsync(ct);
            await db.Notifications.Where(n => candidates.Contains(n.Id)).ExecuteDeleteAsync(ct);
        }
        await transaction.CommitAsync(ct);
        // Inbox, comunicados/idempotencia, auditoria y suscripciones no se eliminan por esta politica.
        return candidates.Length;
    }
}
