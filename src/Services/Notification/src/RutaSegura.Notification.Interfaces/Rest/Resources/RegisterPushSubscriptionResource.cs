namespace RutaSegura.Notification.Interfaces.Rest.Resources;

public sealed record RegisterPushSubscriptionResource(string Token, string Platform);
public sealed record PushSubscriptionResource(Guid Id, Guid InstallationId, string Platform, bool IsEnabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
