using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.QueryServices;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Internal.QueryServices;

internal sealed class NotificationDeliveryQueryService(IDispatcher dispatcher) : INotificationDeliveryQueryService
{
    public Task<Result<IReadOnlyList<NotificationDeliveryResult>>> Handle(GetFailedNotificationDeliveriesQuery query, CancellationToken cancellationToken = default) => dispatcher.QueryAsync(query, cancellationToken);
}
