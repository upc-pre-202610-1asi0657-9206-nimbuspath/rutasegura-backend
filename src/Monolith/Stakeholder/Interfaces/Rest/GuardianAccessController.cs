using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Monolith.Stakeholder.Application.CommandServices;
using Monolith.Stakeholder.Application.QueryServices;
using Monolith.Stakeholder.Domain.Model.Commands;
using Monolith.Stakeholder.Interfaces.Rest.Resources;
using Monolith.Stakeholder.Interfaces.Rest.Transform;
namespace Monolith.Stakeholder.Interfaces.Rest;
[Route("api/v1/stakeholders/administration"), Authorize(Policy = IamPolicies.OrganizationAdministrator)]
public sealed class GuardianAccessController(IGuardianAccessCommandService commands, IGuardianAccessQueryService queries, ProblemDetailsFactory problems) : StakeholderControllerBase(problems)
{
    [HttpGet("guardians")]
    public async Task<IActionResult> Guardians(CancellationToken ct) => Ok(await queries.GuardiansAsync(OrganizationId, ct));
    [HttpPost("guardians")]
    public async Task<IActionResult> RegisterGuardian(RegisterOperationGuardianResource resource, CancellationToken ct) =>
        StakeholderActionResultAssembler.ToActionResult(this, await commands.RegisterGuardianAsync(OrganizationId, resource.GuardianUserId, resource.PhoneNumber, resource.DocumentNumber, ct), ProblemDetails, value => Ok(value));
    [HttpGet("students")]
    public async Task<IActionResult> Students(CancellationToken ct) => Ok((await queries.RosterAsync(OrganizationId, ct)).Select(s => new
    {
        studentId = s.Id.Identifier, s.GuardianUserId, fullName = s.FullName.ToString(), s.BirthDate, s.SchoolName, s.SchoolGrade, s.Status,
        pickupLocation = StakeholderResourceAssembler.ToResource(s), photoUrl = s.Photo is null ? null : StakeholderResourceAssembler.PhotoUrl(s.Id.Identifier)
    }));
    [HttpPost("students")]
    public async Task<IActionResult> RegisterStudent(RegisterOperationStudentResource resource, CancellationToken ct)
    {
        var s = resource.Student; var p = s.PickupLocation;
        var result = await commands.RegisterStudentAsync(OrganizationId, new RegisterStudentCommand(resource.GuardianUserId, s.FirstName, s.LastName, s.BirthDate, s.SchoolGrade, s.SchoolName, p.Address, p.Reference, p.Latitude, p.Longitude), ct);
        return StakeholderActionResultAssembler.ToActionResult(this, result, ProblemDetails, value => StatusCode(201, new { studentId = value.Id.Identifier, guardianUserId = value.GuardianUserId }));
    }
    [HttpGet("guardian-links")]
    public async Task<IActionResult> Links(CancellationToken ct) => Ok((await queries.ListAsync(OrganizationId, ct)).Select(GuardianAccessAssembler.ToResource));
    [HttpPost("guardian-links/{guardianId:guid}/{studentId:guid}/verify")]
    public Task<IActionResult> Verify(Guid guardianId, Guid studentId, ReviewGuardianAccessResource resource, CancellationToken ct) => Review(guardianId, studentId, resource, true, ct);
    [HttpPost("guardian-links/{guardianId:guid}/{studentId:guid}/revoke")]
    public Task<IActionResult> Revoke(Guid guardianId, Guid studentId, ReviewGuardianAccessResource resource, CancellationToken ct) => Review(guardianId, studentId, resource, false, ct);
    private async Task<IActionResult> Review(Guid guardianId, Guid studentId, ReviewGuardianAccessResource resource, bool verify, CancellationToken ct) =>
        StakeholderActionResultAssembler.ToActionResult(this, await commands.ReviewAsync(OrganizationId, CurrentUserId, guardianId, studentId, verify, resource.Reference, ct), ProblemDetails, value => Ok(GuardianAccessAssembler.ToResource(value)));
}
