using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Administration.Application.CommandServices;
using Monolith.Administration.Interfaces.Rest.Transform;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
namespace Monolith.Administration.Interfaces.Rest;
[ApiController, Route("api/v1/routes/{routeId:guid}/itinerary")]
[Authorize(Policy = IamPolicies.OrganizationAdministrator)]
public sealed class RouteItinerariesController(IRouteItineraryCommandService service, ProblemDetailsFactory problems) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Calculate(Guid routeId, CancellationToken ct)
    {
        var result = await service.CalculateAsync(User.GetOrganizationId() ?? Guid.Empty, routeId, ct);
        return AdministrationActionResultAssembler.ToActionResult(this, result, problems,
            value => Ok(new { geometry = JsonSerializer.Deserialize<JsonElement>(value.GeometryGeoJson), value.DistanceMeters, value.DurationSeconds, value.CalculatedAt }));
    }
}
