using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Application.Internal.Transform;

internal static class PushSubscriptionResultFromEntityAssembler
{
    public static PushSubscriptionResult ToResultFromEntity(PushSubscription subscription) => new(subscription.Id, subscription.InstallationId,
        subscription.Platform.ToString(), subscription.IsEnabled, subscription.CreatedAt, subscription.UpdatedAt);
}
