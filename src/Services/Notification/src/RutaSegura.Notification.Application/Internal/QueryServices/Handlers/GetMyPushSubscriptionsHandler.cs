using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.Authorization;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Internal.Transform;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Repositories;

namespace RutaSegura.Notification.Application.Internal.QueryServices.Handlers;

internal sealed class GetMyPushSubscriptionsHandler(IPushSubscriptionRepository subscriptions, INotificationUserContext context)
    : IQueryHandler<GetMyPushSubscriptionsQuery, IReadOnlyList<PushSubscriptionResult>>
{
    public async Task<Result<IReadOnlyList<PushSubscriptionResult>>> HandleAsync(GetMyPushSubscriptionsQuery query, CancellationToken cancellationToken)
    {
        var current = NotificationAccess.Current(context);
        if (current.IsFailure) return current.FirstError;
        var user = current.Value;
        var list = await subscriptions.ListEnabledAsync(user.OrganizationId, user.UserId, cancellationToken);
        return Result<IReadOnlyList<PushSubscriptionResult>>.Success(list
            .Where(s => s.OrganizationId == user.OrganizationId && s.UserId == user.UserId && s.IsEnabled)
            .Select(PushSubscriptionResultFromEntityAssembler.ToResultFromEntity).ToArray());
    }
}
