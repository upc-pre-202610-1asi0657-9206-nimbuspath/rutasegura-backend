namespace RutaSegura.Notification.Application.Internal.Policies;

public sealed record NotificationChannelPolicy(bool PushEnabled, int MaxAttempts = 5);
