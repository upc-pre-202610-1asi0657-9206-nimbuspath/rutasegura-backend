using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Model.Commands;

public sealed record RegisterPushSubscriptionCommand(Guid InstallationId, string Token, string Platform) : ICommand<PushSubscriptionResult>
{
    public override string ToString() => $"RegisterPushSubscriptionCommand {{ InstallationId = {InstallationId}, Token = [REDACTED] }}";
}
