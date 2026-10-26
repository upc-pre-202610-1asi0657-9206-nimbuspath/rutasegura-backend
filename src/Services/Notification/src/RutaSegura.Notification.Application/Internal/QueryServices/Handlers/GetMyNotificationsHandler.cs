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

internal sealed class GetMyNotificationsHandler(NotificationInboxReader reader, INotificationUserContext context)
    : IQueryHandler<GetMyNotificationsQuery, IReadOnlyList<NotificationResult>>
{
    public async Task<Result<IReadOnlyList<NotificationResult>>> HandleAsync(GetMyNotificationsQuery query, CancellationToken cancellationToken)
    {
        var current = NotificationAccess.Current(context);
        if (current.IsFailure) return current.FirstError;
        if (query.Offset < 0 || query.Limit is < 1 or > 100)
            return Error.Validation("NOTIFICATION_INVALID_PAGE", "La pagina requiere offset no negativo y limite entre 1 y 100.");
        var page = await reader.ReadAsync(current.Value, query.Offset, query.Limit, query.UnreadOnly, null, true, cancellationToken);
        return Result<IReadOnlyList<NotificationResult>>.Success(page.Items);
    }
}
