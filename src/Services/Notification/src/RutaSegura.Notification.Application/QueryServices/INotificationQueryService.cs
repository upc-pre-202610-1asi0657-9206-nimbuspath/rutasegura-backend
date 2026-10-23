using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.QueryServices;

public interface INotificationQueryService
{
    Task<Result<IReadOnlyList<NotificationResult>>> Handle(GetMyNotificationsQuery query, CancellationToken cancellationToken = default);
    Task<Result<NotificationResult>> Handle(GetMyNotificationQuery query, CancellationToken cancellationToken = default);
}
