using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Shared.Interfaces.Security;
using Monolith.Stakeholder.Application.QueryServices;
using Monolith.Stakeholder.Interfaces.Rest.Resources;
namespace Monolith.Stakeholder.Interfaces.Rest;
[ApiController, AllowAnonymous, InternalDirectoryToken, Route("internal/v1/notifications")]
public sealed class NotificationDirectoryController(IGuardianAccessQueryService queries) : ControllerBase
{
    [HttpPost("recipients/students")]
    public async Task<IActionResult> Students(DirectoryStudentsRequest request, CancellationToken ct)
    {
        if (request.OrganizationId == Guid.Empty || request.StudentIds is null || request.StudentIds.Count > 500 || request.StudentIds.Any(id => id == Guid.Empty)) return BadRequest();
        return Ok(new { request.OrganizationId, recipients = await queries.CurrentAsync(request.OrganizationId, null, request.StudentIds, null, ct) });
    }
    [HttpPost("recipients/operators")]
    public async Task<IActionResult> Operators(DirectoryOrganizationRequest request, CancellationToken ct) =>
        request.OrganizationId == Guid.Empty ? BadRequest() : Ok(new { request.OrganizationId, recipients = (await queries.OperatorsAsync(request.OrganizationId, ct)).Select(userId => new { request.OrganizationId, userId, studentId = (Guid?)null }) });
    [HttpPost("recipients/announcement")]
    public async Task<IActionResult> Announcement(DirectoryAnnouncementRequest request, CancellationToken ct)
    {
        if (request.OrganizationId == Guid.Empty) return BadRequest();
        if (request.RouteId is { } route && !await queries.OwnsRouteAsync(request.OrganizationId, route, ct)) return NotFound();
        var eligible = await queries.CurrentAsync(request.OrganizationId, null, null, request.RouteId, ct);
        var recipients = eligible.Select(recipient => recipient.UserId).Distinct()
            .Select(userId => new { request.OrganizationId, userId, studentId = (Guid?)null });
        return Ok(new { request.OrganizationId, recipients });
    }
    [HttpPost("student-links")]
    public async Task<IActionResult> Links(DirectoryUserRequest request, CancellationToken ct) =>
        request.OrganizationId == Guid.Empty || request.UserId == Guid.Empty ? BadRequest() : Ok(new { request.OrganizationId, request.UserId, studentIds = (await queries.CurrentAsync(request.OrganizationId, request.UserId, null, null, ct, requireActiveEnrollment: false)).Select(g => g.StudentId).Distinct() });
    [HttpPost("user-contexts")]
    public async Task<IActionResult> Contexts(DirectoryContextsRequest request, CancellationToken ct) =>
        request.UserId == Guid.Empty ? BadRequest() : Ok(new { request.UserId, organizationIds = (await queries.CurrentAsync(null, request.UserId, null, null, ct, requireActiveEnrollment: false)).Select(g => g.OrganizationId).Distinct() });
    [HttpPost("guardian-students")]
    public async Task<IActionResult> GuardianStudents(DirectoryGuardianRequest request, CancellationToken ct) =>
        request.UserId == Guid.Empty || request.OrganizationId == Guid.Empty ? BadRequest() : Ok(new { request.UserId, request.OrganizationId, studentIds = (await queries.CurrentAsync(request.OrganizationId, request.UserId, null, null, ct, requireActiveEnrollment: false)).Select(g => g.StudentId).Distinct() });
}
