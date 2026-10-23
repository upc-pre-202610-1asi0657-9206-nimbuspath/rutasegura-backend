using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Internal.OutboundServices;

public interface IPushNotificationSender
{
    Task<ChannelSendResult> SendAsync(NotificationResult notification, string registrationToken,
        CancellationToken cancellationToken);
}
