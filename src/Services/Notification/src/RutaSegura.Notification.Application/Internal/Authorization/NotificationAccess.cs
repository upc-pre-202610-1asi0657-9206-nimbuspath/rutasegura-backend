using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Model.Aggregates;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Domain.Model.ValueObjects;

namespace RutaSegura.Notification.Application.Internal.Authorization;

internal static class NotificationAccess
{
    internal static Result<NotificationUser> Current(INotificationUserContext context)
    {
        var user = context.Current;
        return user is null || user.UserId == Guid.Empty || user.OrganizationId == Guid.Empty
            ? Error.Unauthorized("NOTIFICATION_AUTHENTICATION_REQUIRED", "Se requiere identidad y organizacion validas.")
            : user;
    }

    internal static async Task<bool> CanReadAsync(NotificationUser user, NotificationEntity notification,
        INotificationRecipientDirectory directory, IAnnouncementRepository announcements, CancellationToken cancellationToken)
    {
        if (notification.OrganizationId != user.OrganizationId || notification.RecipientUserId != user.UserId)
            return false;
        if (notification.StudentId is { } student)
            return (await directory.CurrentStudentLinksAsync(user.OrganizationId, user.UserId, cancellationToken)).Contains(student);
        if (notification.Category == NotificationCategory.Announcement)
        {
            var announcement = await announcements.FindByRequestAsync(user.OrganizationId, notification.SourceMessageId, cancellationToken);
            if (announcement is null || announcement.Id != notification.AnnouncementId) return false;
            var audience = await directory.ForAnnouncementAsync(user.OrganizationId, announcement.RouteId, cancellationToken);
            return audience.IsSuccess && audience.Value.Any(r => r.OrganizationId == user.OrganizationId && r.UserId == user.UserId && r.StudentId is null);
        }
        if (notification.Category == NotificationCategory.Sos)
            return (await directory.OperatorsAsync(user.OrganizationId, cancellationToken))
                .Any(r => r.OrganizationId == user.OrganizationId && r.UserId == user.UserId && r.StudentId is null);
        return true;
    }

    internal static readonly Error NotFound = Error.NotFound("NOTIFICATION_NOT_FOUND", "El aviso no existe o no esta disponible para este usuario.");
}
