namespace RutaSegura.Notification.Application.Model.Results;

public sealed record NotificationPageResult(IReadOnlyList<NotificationResult> Items, string? NextCursor)
{
    public bool HasMore => NextCursor is not null;
}
