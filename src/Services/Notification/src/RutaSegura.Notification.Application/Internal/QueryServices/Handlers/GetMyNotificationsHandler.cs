using RutaSegura.Notification.Application.Internal.Transform;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Internal.Authorization;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.ValueObjects;

namespace RutaSegura.Notification.Application.Internal.QueryServices.Handlers;

internal sealed class GetMyNotificationsHandler(INotificationRepository notifications,
    INotificationRecipientDirectory directory, INotificationUserContext context, IAnnouncementRepository announcements)
    : IQueryHandler<GetMyNotificationsQuery, IReadOnlyList<NotificationResult>>
{
    public async Task<Result<IReadOnlyList<NotificationResult>>> HandleAsync(GetMyNotificationsQuery query, CancellationToken cancellationToken)
    {
        var current = NotificationAccess.Current(context);
        if (current.IsFailure) return current.FirstError;
        if (query.Offset < 0 || query.Limit is < 1 or > 100)
            return Error.Validation("NOTIFICATION_INVALID_PAGE", "La pagina requiere offset no negativo y limite entre 1 y 100.");
        var user = current.Value;
        var links = await directory.CurrentStudentLinksAsync(user.OrganizationId, user.UserId, cancellationToken);
        var items = await notifications.ListAsync(new InboxFilter(user.OrganizationId, user.UserId,
            links, query.Offset, query.Limit, query.UnreadOnly), cancellationToken);
        // El repositorio aplica estos filtros antes de paginar; el nucleo tambien impide devolver objetos ajenos.
        var authorized = new List<NotificationResult>();
        foreach (var item in items)
        {
            if (item.OrganizationId != user.OrganizationId || item.RecipientUserId != user.UserId ||
                (item.StudentId is { } student && !links.Contains(student))) continue;
            // Los comunicados y SOS sin alumno requieren validar su audiencia vigente tambien.
            if (item.StudentId is null && !await NotificationAccess.CanReadAsync(user, item, directory, announcements, cancellationToken)) continue;
            authorized.Add(NotificationResultFromEntityAssembler.ToResultFromEntity(item));
        }
        return Result<IReadOnlyList<NotificationResult>>.Success(authorized);
    }
}
