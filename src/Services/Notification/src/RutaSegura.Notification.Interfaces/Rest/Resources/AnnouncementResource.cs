namespace RutaSegura.Notification.Interfaces.Rest.Resources;

public sealed record AnnouncementResource(Guid AnnouncementId, bool Created, int NotificationsCreated);
