namespace RutaSegura.Notification.Infrastructure.Messaging.Inbound;

public sealed record NotificationIncomingMessage(string? MessageId, string? MessageType, string? CorrelationId,
    string RoutingKey, IDictionary<string, object?>? Headers, ReadOnlyMemory<byte> Body,
    string? ContentType = "application/json", long UnixTimestamp = 0);

public sealed class InvalidNotificationMessageException(string code) : Exception("Mensaje de integracion invalido.")
{
    public string Code { get; } = code;
}
