using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Monolith.Stakeholder.Application.CommandServices;
using Monolith.Stakeholder.Application.QueryServices;
using Monolith.Stakeholder.Domain.Model.Commands;
using Monolith.Stakeholder.Domain.Model.Queries;
using Monolith.Stakeholder.Interfaces.Rest.Resources;
using Monolith.Stakeholder.Interfaces.Rest.Transform;
using Swashbuckle.AspNetCore.Annotations;

namespace Monolith.Stakeholder.Interfaces.Rest;

/// <summary>The administrator of the transport company sees who enrolled in its routes and can end an enrollment.</summary>
[Route("api/v1/stakeholders/enrollments")]
[Authorize(Policy = IamPolicies.OrganizationAdministrator)]
[SwaggerTag("Stakeholder · Enrollments in the routes of the organization (administrator).")]
public class EnrollmentsController(
    IStakeholderCommandService commandService,
    IStakeholderQueryService queryService,
    ProblemDetailsFactory problemDetailsFactory) : StakeholderControllerBase(problemDetailsFactory)
{
    [HttpGet]
    [SwaggerOperation(Summary = "Enrollments of my organization", OperationId = "GetOrganizationEnrollments")]
    public async Task<IActionResult> GetEnrollments([FromQuery] Guid? routeId, [FromQuery] bool onlyActive = true,
        CancellationToken cancellationToken = default)
    {
        var rows = await queryService.Handle(new GetOrganizationEnrollmentsQuery(OrganizationId, routeId, onlyActive),
            cancellationToken);
        return Ok(rows.Select(row => StakeholderResourceAssembler.ToResource(row, includeGuardianEmail: true)));
    }

    [HttpPost("{enrollmentId:guid}/cancel")]
    [SwaggerOperation(Summary = "End an enrollment (frees the seat on the route)", OperationId = "CancelEnrollment")]
    public async Task<IActionResult> CancelEnrollment(Guid enrollmentId, CancelEnrollmentResource? resource,
        CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(
            new CancelEnrollmentCommand(OrganizationId, enrollmentId, resource?.Reason), cancellationToken);
        return StakeholderActionResultAssembler.ToActionResult(this, result, ProblemDetails,
            enrollment => Ok(StakeholderResourceAssembler.ToResource(enrollment)));
    }
}
