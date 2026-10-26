using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Infrastructure.Channels;
using RutaSegura.Notification.Infrastructure.Channels.Fcm;

namespace RutaSegura.Notification.Api.Operations;

/// <summary>Prueba manual local del MISMO adaptador productivo. No abre HTTP ni escribe avisos o destinatarios ficticios.</summary>
public static class NotificationFcmSmoke
{
    public static async Task<int> RunAsync(IConfiguration configuration)
    {
        var project = configuration["NotificationFcm:ProjectId"];
        var file = configuration["NotificationFcmTest:TokenFile"];
        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        { Console.Error.WriteLine("La prueba FCM requiere ProjectId, TokenFile y credenciales ADC externas."); return 1; }
        try
        {
            var info = new FileInfo(file);
            if (info.Length > PushSubscription.MaxTokenLength + 4) throw new FormatException();
            var token = (await File.ReadAllTextAsync(file)).Trim();
            if (token.Length is < 1 or > PushSubscription.MaxTokenLength || token.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) throw new FormatException();
            var options = new NotificationFcmOptions { Enabled = true, ProjectId = project };
            using var transport = new FcmSdkTransport(options);
            var sender = new FcmPushNotificationSender(transport, options);
            var now = DateTimeOffset.UtcNow;
            var id = Guid.CreateVersion7();
            var result = await sender.SendAsync(new NotificationResult(id, Guid.NewGuid(), Guid.NewGuid(), null, null,
                "Test", "Normal", "Prueba", "Prueba", now, now, null), token, CancellationToken.None);
            if (!result.Accepted) { Console.Error.WriteLine($"FCM_TEST_REJECTED: {result.ErrorCode}"); return 2; }
            Console.WriteLine($"FCM_TEST_ACCEPTED NotificationId={id:D}");
            Console.WriteLine("Firebase acepto el envio. Confirma su recepcion en el navegador; aceptacion no equivale a visualizacion ni lectura.");
            return 0;
        }
        catch (Exception) { Console.Error.WriteLine("FCM_TEST_FAILED: revisa los archivos locales, proyecto y credenciales ADC; no se muestran sus contenidos."); return 1; }
    }
}
