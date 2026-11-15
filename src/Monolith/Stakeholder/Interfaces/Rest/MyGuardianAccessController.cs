using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Monolith.Stakeholder.Application.QueryServices;
namespace Monolith.Stakeholder.Interfaces.Rest;
[Route("api/v1/stakeholders/me/guardian-links"), Authorize(Policy = IamPolicies.Guardian)]
public sealed class MyGuardianAccessController(IGuardianAccessQueryService queries, ProblemDetailsFactory problems) : StakeholderControllerBase(problems)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok((await queries.ForGuardianAsync(CurrentUserId, ct)).Select(g => new
    { g.OrganizationId, g.StudentId, g.Status, g.RequestedAt, g.ReviewedAt }));
}
