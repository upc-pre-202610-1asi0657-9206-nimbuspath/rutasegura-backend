using FirebaseAdmin.Messaging;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Infrastructure.Channels.Fcm;

public interface IFcmTransport
{
    Task<string> SendAsync(Message message, CancellationToken cancellationToken);
}

public sealed class FcmPushNotificationSender(IFcmTransport transport, NotificationFcmOptions options) : IPushNotificationSender
{
    public async Task<ChannelSendResult> SendAsync(NotificationResult notification, string registrationToken, CancellationToken cancellationToken)
    {
        if (!options.Enabled) return new(false, false, null, "FCM_DISABLED");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var id = await transport.SendAsync(CreateMessage(notification, registrationToken), timeout.Token);
            return new(true, false, id, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (FirebaseMessagingException ex)
        {
            return Classify(ex.MessagingErrorCode);
        }
        catch (Exception) { return new(false, true, null, "FCM_UNAVAILABLE"); }
    }

    public static ChannelSendResult Classify(MessagingErrorCode? error)
    {
        var code = error switch
        {
            MessagingErrorCode.Unregistered => "FCM_UNREGISTERED",
            MessagingErrorCode.SenderIdMismatch => "FCM_SENDER_ID_MISMATCH",
            MessagingErrorCode.QuotaExceeded => "FCM_QUOTA_EXCEEDED",
            MessagingErrorCode.Unavailable => "FCM_UNAVAILABLE",
            MessagingErrorCode.Internal => "FCM_INTERNAL",
            MessagingErrorCode.InvalidArgument => "FCM_INVALID_ARGUMENT",
            _ => "FCM_CONFIGURATION_ERROR"
        };
        return new(false, error is MessagingErrorCode.QuotaExceeded or MessagingErrorCode.Unavailable or MessagingErrorCode.Internal, null, code);
    }

    public static Message CreateMessage(NotificationResult notification, string token) => new()
    {
        // Compatibilidad explicita con el contrato de registration tokens de fase 2.
        // Firebase 3.7 aun lo soporta; migrar conjuntamente cliente + tipo de destino persistido a FID.
#pragma warning disable CS0618
        Token = token,
#pragma warning restore CS0618
        // Nunca nombres, GPS, alumno, incidencia ni contenido libre en una pantalla bloqueada.
        Notification = new FirebaseAdmin.Messaging.Notification { Title = "RutaSegura", Body = "Tienes un nuevo aviso. Ingresa a RutaSegura para revisarlo." },
        Data = new Dictionary<string, string> { ["notificationId"] = notification.Id.ToString("D") },
        Webpush = new WebpushConfig
        {
            Headers = new Dictionary<string, string> { ["TTL"] = "60", ["Urgency"] = notification.Priority == "Critical" ? "high" : "normal" },
            Notification = new WebpushNotification { Tag = notification.Id.ToString("N") }
        },
        Android = new AndroidConfig { TimeToLive = TimeSpan.FromMinutes(1), Priority = notification.Priority == "Critical" ? Priority.High : Priority.Normal },
        Apns = new ApnsConfig { Headers = new Dictionary<string, string> { ["apns-expiration"] = "0" } }
    };
}
