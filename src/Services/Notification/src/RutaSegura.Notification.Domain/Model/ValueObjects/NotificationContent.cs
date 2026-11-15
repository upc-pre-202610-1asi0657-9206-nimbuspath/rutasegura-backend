using RutaSegura.BuildingBlocks.Domain;

namespace RutaSegura.Notification.Domain.Model.ValueObjects;

public sealed record NotificationContent
{
    public const int MaxTitleLength = 120;
    public const int MaxBodyLength = 2000;

    private NotificationContent(string title, string body) => (Title, Body) = (title, body);

    public string Title { get; }
    public string Body { get; }

    public static NotificationContent Create(string title, string body)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitleLength ||
            string.IsNullOrWhiteSpace(body) || body.Trim().Length > MaxBodyLength)
            throw new DomainException("NOTIFICATION_INVALID_CONTENT", "El aviso requiere titulo y contenido dentro de los limites permitidos.");
        return new(title.Trim(), body.Trim());
    }
}
