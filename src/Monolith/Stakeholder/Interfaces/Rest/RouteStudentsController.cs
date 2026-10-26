using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Interfaces.ACL;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Monolith.Stakeholder.Application.QueryServices;
using Monolith.Stakeholder.Domain.Model.Queries;
using Monolith.Stakeholder.Interfaces.Rest.Transform;
using Swashbuckle.AspNetCore.Annotations;

namespace Monolith.Stakeholder.Interfaces.Rest;

/// <summary>
///     Children riding a route with their name, grade, photo, stop and guardian phone: the list the driver needs to
///     register boardings (Trip &amp; Tracking only knows student ids).
/// </summary>
[SwaggerTag("Stakeholder · Children of a route (administrator and driver).")]
public class RouteStudentsController(IStakeholderQueryService queryService, ProblemDetailsFactory problemDetailsFactory)
    : StakeholderControllerBase(problemDetailsFactory)
{
    /// <summary>The administrator sees any route of its organization; a driver only the routes assigned to it.</summary>
    [HttpGet("api/v1/stakeholders/routes/{routeId:guid}/students")]
    [Authorize(Policy = IamPolicies.OrganizationStaff)]
    [SwaggerOperation(Summary = "Children of a route", OperationId = "GetRouteStudents")]
    public async Task<IActionResult> GetRouteStudents(Guid routeId, CancellationToken cancellationToken)
    {
        var result = await queryService.Handle(
            new GetRouteStudentsQuery(OrganizationId, CurrentUserId, CurrentRole, routeId), cancellationToken);
        var isAdmin = string.Equals(CurrentRole, IamRoles.Admin, StringComparison.OrdinalIgnoreCase);
        return StakeholderActionResultAssembler.ToActionResult(this, result, ProblemDetails,
            rows => Ok(rows.Select(row => StakeholderResourceAssembler.ToResource(row, isAdmin))));
    }

    /// <summary>Every child of every route the driver operates (to cache on the phone before going offline).</summary>
    [HttpGet("api/v1/stakeholders/drivers/me/students")]
    [Authorize(Policy = IamPolicies.OrganizationDriver)]
    [SwaggerOperation(Summary = "Children of the routes I operate", OperationId = "GetMyRouteStudents")]
    public async Task<IActionResult> GetMyRouteStudents(CancellationToken cancellationToken)
    {
        var rows = await queryService.Handle(new GetDriverStudentsQuery(OrganizationId, CurrentUserId),
            cancellationToken);
        return Ok(rows.Select(row => StakeholderResourceAssembler.ToResource(row, includeGuardianEmail: false)));
    }
}
