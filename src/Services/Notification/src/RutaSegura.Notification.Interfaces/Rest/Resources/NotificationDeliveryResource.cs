namespace RutaSegura.Notification.Interfaces.Rest.Resources;

public sealed record NotificationDeliveryResource(Guid Id, Guid NotificationId, string Channel, string Status,
    int AttemptCount, int CycleAttemptCount, int ReplayCount, string? LastErrorCode, DateTimeOffset CreatedAt, DateTimeOffset? LastAttemptAt);
public sealed record ReplayNotificationDeliveryResource(Guid RequestId, string ReasonCode);
public sealed record ReplayNotificationDeliveryResponseResource(Guid DeliveryId, Guid ReplayId, bool Scheduled);
