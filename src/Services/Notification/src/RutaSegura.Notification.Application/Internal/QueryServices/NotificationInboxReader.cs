using RutaSegura.Notification.Application.Internal.Authorization;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Internal.Transform;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Domain.Repositories;

namespace RutaSegura.Notification.Application.Internal.QueryServices;

internal sealed class NotificationInboxReader(INotificationRepository notifications, INotificationRecipientDirectory directory,
    IAnnouncementRepository announcements, NotificationCursorCodec cursors)
{
    public async Task<NotificationPageResult> ReadAsync(NotificationUser user, int skip, int limit, bool unread, string? cursor, bool legacy, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        var token = budget.Token;
        var position = cursors.Read(cursor, user.OrganizationId, user.UserId, unread);
        var links = await directory.CurrentStudentLinksAsync(user.OrganizationId, user.UserId, token);
        var items = new List<NotificationResult>();
        var audience = new Dictionary<(NotificationCategory, Guid), bool>();
        var scanned = 0;
        var skipped = 0;
        while (scanned < 2000)
        {
            var batch = await notifications.ListAsync(new(user.OrganizationId, user.UserId, links, 0, 100, unread,
                position.Snapshot, position.BeforeCreatedAt, position.AfterId), token);
            if (batch.Count == 0) return new(items, null);
            foreach (var item in batch)
            {
                token.ThrowIfCancellationRequested();
                scanned++;
                position = position with { BeforeCreatedAt = item.CreatedAt, AfterId = item.Id };
                if (item.OrganizationId != user.OrganizationId || item.RecipientUserId != user.UserId ||
                    (item.StudentId is { } student && !links.Contains(student))) continue;
                if (item.StudentId is null)
                {
                    var key = (item.Category, item.Category == NotificationCategory.Sos ? Guid.Empty : item.SourceMessageId);
                    if (!audience.TryGetValue(key, out var allowed))
                        audience[key] = allowed = await NotificationAccess.CanReadAsync(user, item, directory, announcements, token);
                    if (!allowed) continue;
                }
                if (skipped++ < skip) continue;
                items.Add(NotificationResultFromEntityAssembler.ToResultFromEntity(item));
                if (items.Count == limit) return new(items, cursors.Write(position));
            }
            if (batch.Count < 100) return new(items, null);
        }
        // El contrato con cursor puede avanzar incluso sobre un bloque entero revocado, sin fingir fin del historial.
        if (legacy) throw new TimeoutException("NOTIFICATION_PAGE_SCAN_LIMIT");
        return new(items, cursors.Write(position));
    }
}
