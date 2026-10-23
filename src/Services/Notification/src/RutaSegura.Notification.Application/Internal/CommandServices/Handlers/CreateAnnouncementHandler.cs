using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Internal.Authorization;
using RutaSegura.Notification.Application.Internal.Validators;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Application.Internal.CommandServices.Handlers;

internal sealed class CreateAnnouncementHandler(IAnnouncementRepository announcements,
    INotificationRepository notifications, INotificationRecipientDirectory directory,
    INotificationDispatchQueue dispatchQueue, INotificationUserContext context, TimeProvider clock)
    : ICommandHandler<CreateAnnouncementCommand, CreateAnnouncementResult>
{
    public async Task<Result<CreateAnnouncementResult>> HandleAsync(CreateAnnouncementCommand command, CancellationToken cancellationToken)
    {
        var current = NotificationAccess.Current(context);
        if (current.IsFailure) return current.FirstError;
        var user = current.Value;
        if (!user.CanPublishAnnouncements)
            return Error.Forbidden("ANNOUNCEMENT_ACCESS_DENIED", "No tienes permisos para publicar comunicados.");
        var errors = new CreateAnnouncementValidator().Validate(command).ToArray();
        if (errors.Length > 0) return Result<CreateAnnouncementResult>.Failure(errors);
        var content = NotificationContent.Create(command.Title, command.Body);
        var existing = await announcements.FindByRequestAsync(user.OrganizationId, command.RequestId, cancellationToken);
        if (existing is not null)
        {
            if (existing.AuthorUserId != user.UserId || existing.RouteId != command.RouteId || existing.Content != content)
                return Error.Conflict("ANNOUNCEMENT_REQUEST_CONFLICT", "La solicitud ya corresponde a otro comunicado.");
            return new CreateAnnouncementResult(existing.Id, false, 0);
        }
        var resolved = await directory.ForAnnouncementAsync(user.OrganizationId, command.RouteId, cancellationToken);
        if (resolved.IsFailure) return resolved.FirstError;
        if (resolved.Value.Any(r => r.OrganizationId != user.OrganizationId || r.UserId == Guid.Empty || r.StudentId is not null))
            return Error.Forbidden("NOTIFICATION_INVALID_AUDIENCE", "Los destinatarios estan fuera del alcance del comunicado.");

        var now = clock.GetUtcNow();
        var announcement = Announcement.Create(user.OrganizationId, user.UserId, command.RequestId, command.RouteId, content, now);
        var audience = resolved.Value.Distinct().ToArray();
        var pending = audience.Select(recipient => NotificationEntity.Create(user.OrganizationId, recipient.UserId,
            command.RequestId, "administration.announcement-published.v1", command.RequestId.ToString(),
            NotificationCategory.Announcement, content, now, now, announcementId: announcement.Id)).ToArray();
        announcements.Add(announcement);
        foreach (var notification in pending)
        {
            notifications.Add(notification);
            dispatchQueue.Enqueue(notification);
        }
        return new CreateAnnouncementResult(announcement.Id, true, pending.Length);
    }
}
