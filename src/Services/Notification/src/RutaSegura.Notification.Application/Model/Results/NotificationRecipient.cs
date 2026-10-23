namespace RutaSegura.Notification.Application.Model.Results;

public sealed record NotificationRecipient(Guid OrganizationId, Guid UserId, Guid? StudentId = null);
