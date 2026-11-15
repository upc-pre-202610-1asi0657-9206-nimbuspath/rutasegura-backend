using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Model.Queries;

public sealed record GetFailedNotificationDeliveriesQuery(int Offset = 0, int Limit = 25) : IQuery<IReadOnlyList<NotificationDeliveryResult>>;
