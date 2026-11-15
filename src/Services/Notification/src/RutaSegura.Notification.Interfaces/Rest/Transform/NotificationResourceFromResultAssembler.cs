using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Interfaces.Rest.Resources;

namespace RutaSegura.Notification.Interfaces.Rest.Transform;

public static class NotificationResourceFromResultAssembler
{
    public static NotificationResource ToResourceFromResult(NotificationResult result) =>
        new(result.Id, result.OrganizationId, result.RecipientUserId, result.TripId, result.StudentId,
            result.Category, result.Priority, result.Title, result.Body, result.OccurredAt, result.CreatedAt, result.ReadAt);
}
