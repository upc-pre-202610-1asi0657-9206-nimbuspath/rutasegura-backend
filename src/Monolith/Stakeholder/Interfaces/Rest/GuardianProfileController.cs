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

/// <summary>Contact data of the authenticated parent (the company and the driver call this phone).</summary>
[Route("api/v1/stakeholders/me/profile")]
[Authorize(Policy = IamPolicies.Guardian)]
[SwaggerTag("Stakeholder · Profile of the authenticated parent.")]
public class GuardianProfileController(
    IStakeholderCommandService commandService,
    IStakeholderQueryService queryService,
    ProblemDetailsFactory problemDetailsFactory) : StakeholderControllerBase(problemDetailsFactory)
{
    [HttpGet]
    [SwaggerOperation(Summary = "My guardian profile", OperationId = "GetMyGuardianProfile")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken) =>
        Ok(StakeholderResourceAssembler.ToResource(
            await queryService.Handle(new GetGuardianProfileQuery(CurrentUserId), cancellationToken)));

    [HttpPut]
    [SwaggerOperation(Summary = "Create or update my contact data", OperationId = "UpsertMyGuardianProfile")]
    public async Task<IActionResult> UpsertProfile(UpsertGuardianProfileResource resource,
        CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(
            new UpsertGuardianProfileCommand(CurrentUserId, resource.PhoneNumber, resource.DocumentNumber),
            cancellationToken);
        if (!result.IsSuccess)
            return StakeholderActionResultAssembler.ToActionResult(this, result, ProblemDetails, _ => Ok());
        return await GetProfile(cancellationToken);
    }
}
