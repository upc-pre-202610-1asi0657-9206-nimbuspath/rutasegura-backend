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
    INotificationRecipientDirectory directory, INotificationUserContext context)
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
        return Result<IReadOnlyList<NotificationResult>>.Success(items.Where(n =>
            n.OrganizationId == user.OrganizationId && n.RecipientUserId == user.UserId &&
            (n.StudentId is null || links.Contains(n.StudentId.Value)))
            .Select(NotificationResultFromEntityAssembler.ToResultFromEntity).ToArray());
    }
}
