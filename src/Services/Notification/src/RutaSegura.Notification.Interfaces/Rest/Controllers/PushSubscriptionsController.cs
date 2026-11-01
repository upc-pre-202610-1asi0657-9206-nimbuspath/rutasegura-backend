using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Application.QueryServices;
using RutaSegura.Notification.Interfaces.Rest.Resources;
using RutaSegura.Notification.Interfaces.Rest.Transform;

namespace RutaSegura.Notification.Interfaces.Rest.Controllers;

[ApiController]
[Authorize(Policy = "notification-user")]
[Route("api/v1/push-subscriptions")]
[RequestSizeLimit(16384)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PushSubscriptionsController(INotificationCommandService commands, INotificationQueryService queries) : NotificationControllerBase
{
    [HttpGet]
    [ProducesResponseType<PushSubscriptionResource[]>(200)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await queries.Handle(new GetMyPushSubscriptionsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value.Select(PushSubscriptionAssemblers.ToResource)) : Failure(result.FirstError);
    }

    [HttpPut("{installationId:guid}")]
    [ProducesResponseType<PushSubscriptionResource>(200)]
    public async Task<IActionResult> Register(Guid installationId, RegisterPushSubscriptionResource resource, CancellationToken cancellationToken)
    {
        var result = await commands.Handle(PushSubscriptionAssemblers.ToCommand(installationId, resource), cancellationToken);
        return result.IsSuccess ? Ok(PushSubscriptionAssemblers.ToResource(result.Value)) : Failure(result.FirstError);
    }

    [HttpDelete("{subscriptionId:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<IActionResult> Disable(Guid subscriptionId, CancellationToken cancellationToken)
    {
        var result = await commands.Handle(new DisablePushSubscriptionCommand(subscriptionId), cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result.FirstError);
    }
}
