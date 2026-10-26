using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.Authorization;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Repositories;

namespace RutaSegura.Notification.Application.Internal.QueryServices.Handlers;

internal sealed class GetFailedNotificationDeliveriesHandler(INotificationUserContext context, INotificationRecipientDirectory directory,
    INotificationDeliveryAdministrationRepository administration) : IQueryHandler<GetFailedNotificationDeliveriesQuery, IReadOnlyList<NotificationDeliveryResult>>
{
    public async Task<Result<IReadOnlyList<NotificationDeliveryResult>>> HandleAsync(GetFailedNotificationDeliveriesQuery query, CancellationToken ct)
    {
        var user = await NotificationAdministratorAccess.RequireAsync(context, directory, ct);
        if (user.IsFailure) return user.FirstError;
        if (query.Offset < 0 || query.Limit is < 1 or > 100) return Error.Validation("NOTIFICATION_INVALID_PAGE", "Paginacion invalida.");
        return (await administration.ListFailedAsync(user.Value.OrganizationId, query.Offset, query.Limit, ct))
            .Select(d => new NotificationDeliveryResult(d.Id, d.NotificationId, d.Channel.ToString(), d.Status.ToString(), d.AttemptCount,
                d.CycleAttemptCount, d.ReplayCount, d.LastErrorCode, d.CreatedAt, d.LastAttemptAt)).ToArray();
    }
}
