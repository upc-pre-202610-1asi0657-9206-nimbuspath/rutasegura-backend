using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Model.Queries;

public sealed record GetMyPushSubscriptionsQuery : IQuery<IReadOnlyList<PushSubscriptionResult>>;
