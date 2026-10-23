using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.CommandServices;

public interface INotificationCommandService
{
    Task<Result<ReceiveTripEventResult>> Handle(ReceiveTripEventCommand command, CancellationToken cancellationToken = default);
    Task<Result<CreateAnnouncementResult>> Handle(CreateAnnouncementCommand command, CancellationToken cancellationToken = default);
    Task<Result<NotificationResult>> Handle(MarkNotificationAsReadCommand command, CancellationToken cancellationToken = default);
    Task<Result<PushSubscriptionResult>> Handle(RegisterPushSubscriptionCommand command, CancellationToken cancellationToken = default);
    Task<Result<PushSubscriptionResult>> Handle(DisablePushSubscriptionCommand command, CancellationToken cancellationToken = default);
}
