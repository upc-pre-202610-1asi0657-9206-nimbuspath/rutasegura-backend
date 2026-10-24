namespace RutaSegura.Notification.Application.Model.Results;

public sealed record PushSubscriptionResult(Guid Id, Guid InstallationId, string Platform, bool IsEnabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
