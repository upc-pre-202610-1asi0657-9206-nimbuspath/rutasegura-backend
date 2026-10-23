using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Model.Aggregates;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

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
        INotificationRecipientDirectory directory, CancellationToken cancellationToken)
    {
        if (notification.OrganizationId != user.OrganizationId || notification.RecipientUserId != user.UserId)
            return false;
        return notification.StudentId is null ||
            (await directory.CurrentStudentLinksAsync(user.OrganizationId, user.UserId, cancellationToken))
            .Contains(notification.StudentId.Value);
    }

    internal static readonly Error NotFound = Error.NotFound("NOTIFICATION_NOT_FOUND", "El aviso no existe o no esta disponible para este usuario.");
}
