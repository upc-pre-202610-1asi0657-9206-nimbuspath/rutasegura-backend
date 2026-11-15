namespace RutaSegura.Notification.Application.Model.Results;

public sealed record NotificationResult(Guid Id, Guid OrganizationId, Guid RecipientUserId, Guid? TripId,
    Guid? StudentId, string Category, string Priority, string Title, string Body,
    DateTimeOffset OccurredAt, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
