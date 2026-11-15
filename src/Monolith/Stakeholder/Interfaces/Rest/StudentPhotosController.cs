using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Monolith.Stakeholder.Application.QueryServices;
using Monolith.Stakeholder.Domain.Model.Queries;
using Monolith.Stakeholder.Interfaces.Rest.Transform;
using Swashbuckle.AspNetCore.Annotations;

namespace Monolith.Stakeholder.Interfaces.Rest;

/// <summary>
///     Photo of a child. Never public (minors, AC-01): the guardian, the administrator of a company the child rides
///     with and the driver of one of those routes may download it, with their token.
/// </summary>
[Route("api/v1/stakeholders/students/{studentId:guid}/photo")]
[Authorize(Policy = IamPolicies.Account)]
[SwaggerTag("Stakeholder · Photo of a child.")]
public class StudentPhotosController(IStakeholderQueryService queryService, ProblemDetailsFactory problemDetailsFactory)
    : StakeholderControllerBase(problemDetailsFactory)
{
    [HttpGet]
    [Produces("image/jpeg", "image/png", "image/webp", "application/problem+json")]
    [SwaggerOperation(Summary = "Download the photo", OperationId = "GetStudentPhoto")]
    public async Task<IActionResult> GetPhoto(Guid studentId, CancellationToken cancellationToken)
    {
        var result = await queryService.Handle(new GetStudentPhotoQuery(studentId, CurrentUserId, CurrentRole,
            User.GetOrganizationId()), cancellationToken);
        return StakeholderActionResultAssembler.ToActionResult(this, result, ProblemDetails, photo =>
        {
            Response.Headers[HeaderNames.CacheControl] = "private, max-age=300";
            return File(photo.Content, photo.ContentType, null, null,
                new EntityTagHeaderValue($"\"{photo.Version}\""));
        });
    }
}
