using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.CommandServices;

public interface INotificationDeliveryCommandService
{
    Task<Result<ReplayNotificationDeliveryResult>> Handle(ReplayNotificationDeliveryCommand command, CancellationToken cancellationToken = default);
}
