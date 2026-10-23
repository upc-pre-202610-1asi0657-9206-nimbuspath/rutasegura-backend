using RutaSegura.Notification.Application.Model.Results;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Application.Internal.Transform;

internal static class NotificationResultFromEntityAssembler
{
    public static NotificationResult ToResultFromEntity(NotificationEntity notification) => new(notification.Id,
        notification.OrganizationId, notification.RecipientUserId, notification.TripId, notification.StudentId,
        notification.Category.ToString(), notification.Priority.ToString(), notification.Content.Title,
        notification.Content.Body, notification.OccurredAt, notification.CreatedAt, notification.ReadAt);
}
