using System.Security.Cryptography;
using System.Text.Json;
using RutaSegura.Notification.Application.Model.Commands;

namespace RutaSegura.Notification.Application.Internal.Transform;

internal static class NotificationEventFingerprint
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Create(ReceiveTripEventCommand command)
    {
        // El tipo concreto evita perder los campos del payload al serializar la clase base.
        var payload = JsonSerializer.SerializeToElement(command.Event, command.Event.GetType(), JsonOptions);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            command.Metadata.MessageId, command.Metadata.MessageType, command.Metadata.SchemaVersion,
            OccurredAt = command.Metadata.OccurredAt.ToUniversalTime(), command.Metadata.CorrelationId,
            Payload = payload
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
