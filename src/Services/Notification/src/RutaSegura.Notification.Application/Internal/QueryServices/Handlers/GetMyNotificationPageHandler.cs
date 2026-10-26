using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.Authorization;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Internal.QueryServices.Handlers;

internal sealed class GetMyNotificationPageHandler(NotificationInboxReader reader, INotificationUserContext context)
    : IQueryHandler<GetMyNotificationPageQuery, NotificationPageResult>
{
    public async Task<Result<NotificationPageResult>> HandleAsync(GetMyNotificationPageQuery query, CancellationToken ct)
    {
        var current = NotificationAccess.Current(context);
        if (current.IsFailure) return current.FirstError;
        if (query.Limit is < 1 or > 100) return Error.Validation("NOTIFICATION_INVALID_PAGE", "El limite debe estar entre 1 y 100.");
        try { return await reader.ReadAsync(current.Value, 0, query.Limit, query.UnreadOnly, query.Cursor, false, ct); }
        catch (FormatException) { return Error.Validation("NOTIFICATION_INVALID_CURSOR", "Cursor invalido, vencido o de otro contexto."); }
    }
}
