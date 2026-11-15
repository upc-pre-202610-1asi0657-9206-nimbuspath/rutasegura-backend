using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RutaSegura.Notification.Application.Internal.OutboundServices;
namespace RutaSegura.Notification.Interfaces.Rest.Controllers;
[ApiController, Authorize(Policy = "notification-account"), Route("api/v1/notifications/contexts")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationContextsController(INotificationAccountContext account, INotificationRecipientDirectory directory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(new { organizationIds = account.IsParent
        ? await directory.CurrentOrganizationIdsAsync(account.UserId, ct)
        : account.OrganizationId is { } org ? new[] { org } : Array.Empty<Guid>() });
}
