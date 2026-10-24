using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Interfaces.Rest.Resources;
using RutaSegura.Notification.Interfaces.Rest.Transform;

namespace RutaSegura.Notification.Interfaces.Rest.Controllers;

[ApiController]
[Authorize(Policy = "notification-admin")]
[Route("api/v1/announcements")]
[RequestSizeLimit(32768)]
public sealed class AnnouncementsController(INotificationCommandService commands) : NotificationControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateAnnouncementResource resource, CancellationToken cancellationToken)
    {
        var result = await commands.Handle(CreateAnnouncementCommandFromResourceAssembler.ToCommandFromResource(resource), cancellationToken);
        if (result.IsFailure) return Failure(result.FirstError);
        var response = AnnouncementResourceFromResultAssembler.ToResourceFromResult(result.Value);
        // No se publica Location ficticia: aun no hay endpoint de detalle administrativo del comunicado.
        return StatusCode(result.Value.Created ? 201 : 200, response);
    }
}
