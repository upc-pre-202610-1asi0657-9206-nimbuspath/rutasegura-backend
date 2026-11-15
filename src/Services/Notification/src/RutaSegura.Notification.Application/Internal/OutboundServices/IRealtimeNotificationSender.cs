using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Internal.OutboundServices;

public interface IRealtimeNotificationSender
{
    Task<ChannelSendResult> SendAsync(NotificationResult notification, CancellationToken cancellationToken);
}
