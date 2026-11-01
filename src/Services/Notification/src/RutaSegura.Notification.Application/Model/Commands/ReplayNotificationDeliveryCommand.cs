using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Model.Commands;

public sealed record ReplayNotificationDeliveryCommand(Guid DeliveryId, Guid RequestId, string ReasonCode) : ICommand<ReplayNotificationDeliveryResult>;
