using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Internal.Authorization;

internal static class NotificationAdministratorAccess
{
    public static async Task<Result<NotificationUser>> RequireAsync(INotificationUserContext context, INotificationRecipientDirectory directory, CancellationToken ct)
    {
        var current = NotificationAccess.Current(context);
        if (current.IsFailure) return current.FirstError;
        if (!current.Value.CanPublishAnnouncements || !(await directory.OperatorsAsync(current.Value.OrganizationId, ct))
            .Any(r => r.OrganizationId == current.Value.OrganizationId && r.UserId == current.Value.UserId && r.StudentId is null))
            return Error.Forbidden("NOTIFICATION_ADMIN_REQUIRED", "Se requiere administrador operativo vigente.");
        return current.Value;
    }
}
