using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Administration.Application.CommandServices;
using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Model.Commands;
using Monolith.Administration.Domain.Model.Queries;
using Monolith.Administration.Interfaces.Rest.Transform;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Swashbuckle.AspNetCore.Annotations;
using Assembler = Monolith.Administration.Interfaces.Rest.Transform.AdministrationResourceFromEntityAssembler;

namespace Monolith.Administration.Interfaces.Rest;

/// <summary>Follow-up of incidents and SOS received from Trip &amp; Tracking. Organization administrators only.</summary>
[Route("api/v1/operations/alerts")]
[Authorize(Policy = IamPolicies.OrganizationAdministrator)]
[SwaggerTag("Administration · Operations: incidents and SOS reported by Trip & Tracking.")]
public class OperationalAlertsController(
    IOperationsMonitoringCommandService commandService,
    IOperationsMonitoringQueryService queryService,
    ProblemDetailsFactory problemDetailsFactory) : AdministrationControllerBase(problemDetailsFactory)
{
    [HttpGet]
    [SwaggerOperation(Summary = "Alerts of the organization (open ones by default)", OperationId = "GetAlerts")]
    public async Task<IActionResult> GetAlerts([FromQuery] bool onlyOpen = true,
        CancellationToken cancellationToken = default)
    {
        var alerts = await queryService.Handle(new GetOperationalAlertsQuery(OrganizationId, onlyOpen),
            cancellationToken);
        return Ok(alerts.Select(Assembler.ToResourceFromEntity));
    }

    [HttpPost("{alertId:guid}/acknowledge")]
    [SwaggerOperation(Summary = "Acknowledge an alert", OperationId = "AcknowledgeAlert")]
    public async Task<IActionResult> Acknowledge(Guid alertId, CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new AcknowledgeAlertCommand(OrganizationId, alertId, CurrentUserId),
            cancellationToken);
        return AdministrationActionResultAssembler.ToActionResult(this, result, ProblemDetails,
            alert => Ok(Assembler.ToResourceFromEntity(alert)));
    }

    [HttpPost("{alertId:guid}/resolve")]
    [SwaggerOperation(Summary = "Close an incident (an SOS is resolved in Trip & Tracking)",
        OperationId = "ResolveAlert")]
    public async Task<IActionResult> Resolve(Guid alertId, CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new ResolveAlertCommand(OrganizationId, alertId, CurrentUserId),
            cancellationToken);
        return AdministrationActionResultAssembler.ToActionResult(this, result, ProblemDetails,
            alert => Ok(Assembler.ToResourceFromEntity(alert)));
    }
}
