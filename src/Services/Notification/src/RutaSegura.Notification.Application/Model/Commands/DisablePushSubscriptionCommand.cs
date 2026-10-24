using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Model.Commands;

public sealed record DisablePushSubscriptionCommand(Guid SubscriptionId) : ICommand<PushSubscriptionResult>;
