using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Interfaces.Rest.Resources;

namespace RutaSegura.Notification.Interfaces.Rest.Transform;

public static class NotificationDeliveryResourceFromResultAssembler
{
    public static NotificationDeliveryResource ToResourceFromResult(NotificationDeliveryResult result) =>
        new(result.Id, result.NotificationId, result.Channel, result.Status, result.AttemptCount, result.CycleAttemptCount,
            result.ReplayCount, result.LastErrorCode, result.CreatedAt, result.LastAttemptAt);
}

public static class ReplayNotificationDeliveryCommandFromResourceAssembler
{
    public static ReplayNotificationDeliveryCommand ToCommandFromResource(Guid deliveryId, ReplayNotificationDeliveryResource resource) =>
        new(deliveryId, resource.RequestId, resource.ReasonCode);
}

public static class ReplayNotificationDeliveryResponseResourceFromResultAssembler
{
    public static ReplayNotificationDeliveryResponseResource ToResourceFromResult(ReplayNotificationDeliveryResult result) =>
        new(result.DeliveryId, result.ReplayId, result.Scheduled);
}
