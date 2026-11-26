using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Monolith.Stakeholder.Application.QueryServices;
using Monolith.Stakeholder.Domain.Model;
using Monolith.Stakeholder.Domain.Model.Queries;
using Monolith.Stakeholder.Interfaces.Rest.Transform;
using Swashbuckle.AspNetCore.Annotations;

namespace Monolith.Stakeholder.Interfaces.Rest;

/// <summary>
///     Before enrolling, the parent browses the open routes (code, company, destination, schedule, free seats) or
///     looks up the route a code belongs to.
/// </summary>
[Route("api/v1/stakeholders/route-offers")]
[Authorize(Policy = IamPolicies.Guardian)]
[SwaggerTag("Stakeholder · Route behind an enrollment code.")]
public class RouteOffersController(IStakeholderQueryService queryService, ProblemDetailsFactory problemDetailsFactory)
    : StakeholderControllerBase(problemDetailsFactory)
{
    [HttpGet]
    [SwaggerOperation(Summary = "Routes open to enrollments, with their codes", OperationId = "GetOpenRouteOffers")]
    public async Task<IActionResult> GetOpenRouteOffers(CancellationToken cancellationToken)
    {
        var offers = await queryService.Handle(new GetOpenRouteOffersQuery(), cancellationToken);
        return Ok(offers.Select(StakeholderResourceAssembler.ToResource));
    }

    [HttpGet("{enrollmentCode}")]
    [SwaggerOperation(Summary = "Route of an enrollment code", OperationId = "GetRouteOffer")]
    public async Task<IActionResult> GetRouteOffer(string enrollmentCode, CancellationToken cancellationToken)
    {
        var offer = await queryService.Handle(new GetRouteOfferQuery(enrollmentCode), cancellationToken);
        return offer is null
            ? StakeholderActionResultAssembler.Problem(this, ProblemDetails, StakeholderError.EnrollmentCodeNotFound,
                "No route uses that enrollment code.")
            : Ok(StakeholderResourceAssembler.ToResource(offer));
    }
}
