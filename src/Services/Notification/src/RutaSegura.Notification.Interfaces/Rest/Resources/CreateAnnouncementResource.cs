namespace RutaSegura.Notification.Interfaces.Rest.Resources;

public sealed record CreateAnnouncementResource(Guid RequestId, string Title, string Body, Guid? RouteId = null);
