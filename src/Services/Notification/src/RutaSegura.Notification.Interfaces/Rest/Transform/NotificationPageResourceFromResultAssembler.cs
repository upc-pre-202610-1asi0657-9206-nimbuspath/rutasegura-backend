using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Interfaces.Rest.Resources;

namespace RutaSegura.Notification.Interfaces.Rest.Transform;

public static class NotificationPageResourceFromResultAssembler
{
    public static NotificationPageResource ToResourceFromResult(NotificationPageResult result) =>
        new(result.Items.Select(NotificationResourceFromResultAssembler.ToResourceFromResult).ToArray(), result.NextCursor, result.HasMore);
}
