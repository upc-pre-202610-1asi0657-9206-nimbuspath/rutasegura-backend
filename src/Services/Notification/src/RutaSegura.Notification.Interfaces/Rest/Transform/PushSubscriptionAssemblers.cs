using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Interfaces.Rest.Resources;

namespace RutaSegura.Notification.Interfaces.Rest.Transform;

public static class PushSubscriptionAssemblers
{
    public static RegisterPushSubscriptionCommand ToCommand(Guid installationId, RegisterPushSubscriptionResource resource) =>
        new(installationId, resource.Token, resource.Platform);
    public static PushSubscriptionResource ToResource(PushSubscriptionResult result) =>
        new(result.Id, result.InstallationId, result.Platform, result.IsEnabled, result.CreatedAt, result.UpdatedAt);
}
