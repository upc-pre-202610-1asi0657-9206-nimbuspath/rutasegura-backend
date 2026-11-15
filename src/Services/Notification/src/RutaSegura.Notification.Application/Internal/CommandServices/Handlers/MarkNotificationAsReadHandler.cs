using RutaSegura.Notification.Application.Internal.Transform;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Internal.Authorization;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Application.Internal.CommandServices.Handlers;

internal sealed class MarkNotificationAsReadHandler(INotificationRepository notifications,
    INotificationRecipientDirectory directory, INotificationUserContext context, TimeProvider clock, IAnnouncementRepository announcements)
    : ICommandHandler<MarkNotificationAsReadCommand, NotificationResult>
{
    public async Task<Result<NotificationResult>> HandleAsync(MarkNotificationAsReadCommand command, CancellationToken cancellationToken)
    {
        var current = NotificationAccess.Current(context);
        if (current.IsFailure) return current.FirstError;
        var user = current.Value;
        var notification = await notifications.GetAsync(user.OrganizationId, command.NotificationId, cancellationToken);
        if (notification is null || !await NotificationAccess.CanReadAsync(user, notification, directory, announcements, cancellationToken))
            return NotificationAccess.NotFound;
        notification.MarkAsRead(clock.GetUtcNow());
        return NotificationResultFromEntityAssembler.ToResultFromEntity(notification);
    }
}
