using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.QueryServices;

public interface INotificationDeliveryQueryService
{
    Task<Result<IReadOnlyList<NotificationDeliveryResult>>> Handle(GetFailedNotificationDeliveriesQuery query, CancellationToken cancellationToken = default);
}
