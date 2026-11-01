using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Internal.CommandServices;

internal sealed class NotificationDeliveryCommandService(IDispatcher dispatcher) : INotificationDeliveryCommandService
{
    public Task<Result<ReplayNotificationDeliveryResult>> Handle(ReplayNotificationDeliveryCommand command, CancellationToken cancellationToken = default) => dispatcher.SendAsync(command, cancellationToken);
}
