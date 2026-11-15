using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Application.QueryServices;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Interfaces.Rest.Resources;
using RutaSegura.Notification.Interfaces.Rest.Transform;

namespace RutaSegura.Notification.Interfaces.Rest.Controllers;

[ApiController]
[Authorize(Policy = "notification-admin")]
[Route("api/v1/notifications/deliveries")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(4096)]
public sealed class NotificationDeliveriesController(INotificationDeliveryCommandService commands, INotificationDeliveryQueryService queries) : NotificationControllerBase
{
    [HttpGet("failed")]
    [ProducesResponseType<NotificationDeliveryResource[]>(200)]
    public async Task<IActionResult> Failed([FromQuery] int offset = 0, [FromQuery] int limit = 25, CancellationToken cancellationToken = default)
    {
        var result = await queries.Handle(new GetFailedNotificationDeliveriesQuery(offset, limit), cancellationToken);
        return result.IsSuccess ? Ok(result.Value.Select(NotificationDeliveryResourceFromResultAssembler.ToResourceFromResult)) : Failure(result.FirstError);
    }
    [HttpPost("{deliveryId:guid}/replay")]
    [ProducesResponseType<ReplayNotificationDeliveryResponseResource>(202)]
    [ProducesResponseType<ReplayNotificationDeliveryResponseResource>(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<IActionResult> Replay(Guid deliveryId, ReplayNotificationDeliveryResource resource, CancellationToken cancellationToken)
    {
        var result = await commands.Handle(ReplayNotificationDeliveryCommandFromResourceAssembler.ToCommandFromResource(deliveryId, resource), cancellationToken);
        return result.IsSuccess ? StatusCode(result.Value.Scheduled ? 202 : 200,
            ReplayNotificationDeliveryResponseResourceFromResultAssembler.ToResourceFromResult(result.Value)) : Failure(result.FirstError);
    }
}
