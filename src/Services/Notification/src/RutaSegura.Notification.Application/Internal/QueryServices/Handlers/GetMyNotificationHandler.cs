using RutaSegura.Notification.Application.Internal.Transform;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Internal.Authorization;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Application.Internal.QueryServices.Handlers;

internal sealed class GetMyNotificationHandler(INotificationRepository notifications,
    INotificationRecipientDirectory directory, INotificationUserContext context, IAnnouncementRepository announcements)
    : IQueryHandler<GetMyNotificationQuery, NotificationResult>
{
    public async Task<Result<NotificationResult>> HandleAsync(GetMyNotificationQuery query, CancellationToken cancellationToken)
    {
        var current = NotificationAccess.Current(context);
        if (current.IsFailure) return current.FirstError;
        if (query.NotificationId == Guid.Empty)
            return Error.Validation("NOTIFICATION_ID_REQUIRED", "Se requiere el identificador del aviso.");
        var user = current.Value;
        var notification = await notifications.GetAsync(user.OrganizationId, query.NotificationId, cancellationToken);
        if (notification is null || !await NotificationAccess.CanReadAsync(user, notification, directory, announcements, cancellationToken))
            return NotificationAccess.NotFound;
        return NotificationResultFromEntityAssembler.ToResultFromEntity(notification);
    }
}
