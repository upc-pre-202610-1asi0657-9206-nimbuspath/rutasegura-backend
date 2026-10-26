using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Application.QueryServices;
using RutaSegura.Notification.Interfaces.Rest.Transform;
using RutaSegura.Notification.Interfaces.Rest.Resources;

namespace RutaSegura.Notification.Interfaces.Rest.Controllers;

[ApiController]
[Authorize(Policy = "notification-user")]
[Route("api/v1/notifications")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(INotificationQueryService queries, INotificationCommandService commands) : NotificationControllerBase
{
    [HttpGet]
    [ProducesResponseType<NotificationResource[]>(200)]
    public async Task<IActionResult> List([FromQuery] int offset = 0, [FromQuery] int limit = 25,
        [FromQuery] bool unreadOnly = false, CancellationToken cancellationToken = default)
    {
        var result = await queries.Handle(new GetMyNotificationsQuery(offset, limit, unreadOnly), cancellationToken);
        return result.IsSuccess ? Ok(result.Value.Select(NotificationResourceFromResultAssembler.ToResourceFromResult)) : Failure(result.FirstError);
    }

    [HttpGet("{notificationId:guid}")]
    [ProducesResponseType<NotificationResource>(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<IActionResult> Get(Guid notificationId, CancellationToken cancellationToken)
    {
        var result = await queries.Handle(new GetMyNotificationQuery(notificationId), cancellationToken);
        return result.IsSuccess ? Ok(NotificationResourceFromResultAssembler.ToResourceFromResult(result.Value)) : Failure(result.FirstError);
    }

    [HttpPatch("{notificationId:guid}/read")]
    [ProducesResponseType<NotificationResource>(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<IActionResult> Read(Guid notificationId, CancellationToken cancellationToken)
    {
        var result = await commands.Handle(new MarkNotificationAsReadCommand(notificationId), cancellationToken);
        return result.IsSuccess ? Ok(NotificationResourceFromResultAssembler.ToResourceFromResult(result.Value)) : Failure(result.FirstError);
    }
}
