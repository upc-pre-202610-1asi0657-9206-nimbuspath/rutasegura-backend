using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Model.Queries;
using Monolith.Iam.Interfaces.ACL;
using Monolith.Shared.Interfaces.Security;

namespace Monolith.Administration.Interfaces.Rest;

/// <summary>Internal published directory. Profile ids never substitute IAM recipient ids.</summary>
[ApiController, AllowAnonymous, InternalDirectoryToken]
[Route("internal/v1/notifications/recipients/drivers")]
public sealed class DriverNotificationDirectoryController(IRosterQueryService roster,
    IRoutePlanningQueryService routes, IIamContextFacade iam) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Find(DriverNotificationDirectoryRequest request, CancellationToken ct)
    {
        if (request.OrganizationId == Guid.Empty || request.DriverUserId == Guid.Empty || request.RouteId == Guid.Empty)
            return BadRequest();
        var recipients = new List<object>();
        if (!await iam.IsOrganizationActiveAsync(request.OrganizationId, ct))
            return Ok(new { request.OrganizationId, recipients });
        var route = request.RouteId is { } routeId
            ? await routes.Handle(new GetRouteByIdQuery(request.OrganizationId, routeId), ct) : null;
        if (request.RouteId is not null && route is null) return NotFound();
        var profiles = request.DriverUserId is { } userId
            ? new[] { await roster.Handle(new GetDriverByUserIdQuery(request.OrganizationId, userId), ct) }
            : (await roster.Handle(new GetAllDriversQuery(request.OrganizationId), ct)).ToArray();
        foreach (var driver in profiles)
        {
            if (driver is null || !driver.Status.IsVerified() ||
                (route is not null && !route.UsesDriver(driver.Id))) continue;
            var user = await iam.FetchUserAccountAsync(driver.UserId.Identifier, ct);
            if (user is null || !user.HasRole("DRIVER") || user.OrganizationId != request.OrganizationId) continue;
            recipients.Add(new { request.OrganizationId, userId = user.UserId, studentId = (Guid?)null });
        }
        return Ok(new { request.OrganizationId, recipients });
    }
}

public sealed record DriverNotificationDirectoryRequest(Guid OrganizationId, Guid? DriverUserId, Guid? RouteId);
