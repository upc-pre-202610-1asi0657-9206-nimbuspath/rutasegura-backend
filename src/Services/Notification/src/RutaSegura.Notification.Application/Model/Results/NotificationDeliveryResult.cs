namespace RutaSegura.Notification.Application.Model.Results;

public sealed record NotificationDeliveryResult(Guid Id, Guid NotificationId, string Channel, string Status, int AttemptCount,
    int CycleAttemptCount, int ReplayCount, string? LastErrorCode, DateTimeOffset CreatedAt, DateTimeOffset? LastAttemptAt);
public sealed record ReplayNotificationDeliveryResult(Guid DeliveryId, Guid ReplayId, bool Scheduled);
