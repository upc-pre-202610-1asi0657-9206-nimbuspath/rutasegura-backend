namespace RutaSegura.Notification.Application.Internal.Policies;

public sealed record NotificationReplayPolicy(int MaxManualReplays = 3);
