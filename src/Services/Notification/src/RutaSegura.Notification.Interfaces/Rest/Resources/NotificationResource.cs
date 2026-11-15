namespace RutaSegura.Notification.Interfaces.Rest.Resources;

public sealed record NotificationResource(Guid Id, Guid OrganizationId, Guid RecipientUserId, Guid? TripId,
    Guid? StudentId, string Category, string Priority, string Title, string Body,
    DateTimeOffset OccurredAt, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
