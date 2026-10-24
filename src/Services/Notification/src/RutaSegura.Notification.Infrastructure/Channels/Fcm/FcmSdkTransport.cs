using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace RutaSegura.Notification.Infrastructure.Channels.Fcm;

public sealed class FcmSdkTransport(NotificationFcmOptions options) : IFcmTransport, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FirebaseApp? _app;

    public async Task<string> SendAsync(Message message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _app ??= FirebaseApp.Create(new AppOptions
            {
                ProjectId = options.ProjectId,
                Credential = await GoogleCredential.GetApplicationDefaultAsync(cancellationToken)
            }, "rutasegura-notification-" + Guid.NewGuid().ToString("N"));
        }
        finally { _gate.Release(); }
        return await FirebaseMessaging.GetMessaging(_app).SendAsync(message, cancellationToken);
    }

    public void Dispose() { _app?.Delete(); _gate.Dispose(); }
}
