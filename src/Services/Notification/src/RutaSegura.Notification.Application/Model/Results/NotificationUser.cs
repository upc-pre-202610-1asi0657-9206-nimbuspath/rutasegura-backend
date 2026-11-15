namespace RutaSegura.Notification.Application.Model.Results;

public sealed record NotificationUser(Guid UserId, Guid OrganizationId, bool CanPublishAnnouncements = false);
