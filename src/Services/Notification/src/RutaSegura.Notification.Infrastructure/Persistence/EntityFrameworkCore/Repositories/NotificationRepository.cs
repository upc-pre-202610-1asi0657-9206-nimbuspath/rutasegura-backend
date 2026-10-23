using Microsoft.EntityFrameworkCore;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories;

public sealed class NotificationRepository(NotificationDbContext context) : INotificationRepository
{
    public Task<NotificationEntity?> GetAsync(Guid organizationId, Guid notificationId, CancellationToken cancellationToken) =>
        context.Notifications.SingleOrDefaultAsync(n => n.OrganizationId == organizationId && n.Id == notificationId, cancellationToken);

    public Task<NotificationEntity?> FindByKeyAsync(NotificationKey key, CancellationToken cancellationToken) =>
        context.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.OrganizationId == key.OrganizationId &&
            n.SourceMessageId == key.SourceMessageId && n.SourceMessageType == key.SourceMessageType &&
            n.RecipientUserId == key.RecipientUserId && n.SubjectId == key.SubjectId, cancellationToken);

    public async Task<IReadOnlyList<NotificationEntity>> ListAsync(InboxFilter filter, CancellationToken cancellationToken)
    {
        if (filter.Offset < 0 || filter.Limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(filter));
        var students = filter.AuthorizedStudentIds.ToArray();
        var query = context.Notifications.AsNoTracking().Where(n =>
            n.OrganizationId == filter.OrganizationId && n.RecipientUserId == filter.RecipientUserId &&
            (n.StudentId == null || students.Contains(n.StudentId.Value)));
        if (filter.UnreadOnly) query = query.Where(n => n.ReadAt == null);
        return await query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Skip(filter.Offset).Take(filter.Limit).ToArrayAsync(cancellationToken);
    }

    public void Add(NotificationEntity notification) => context.Notifications.Add(notification);
}
