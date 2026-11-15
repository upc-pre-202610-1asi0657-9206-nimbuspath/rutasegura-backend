namespace RutaSegura.Notification.Interfaces.Rest.Resources;

public sealed record NotificationPageResource(IReadOnlyList<NotificationResource> Items, string? NextCursor, bool HasMore);
