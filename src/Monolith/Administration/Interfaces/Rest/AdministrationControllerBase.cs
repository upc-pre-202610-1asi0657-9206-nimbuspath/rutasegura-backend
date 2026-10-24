using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;

namespace Monolith.Administration.Interfaces.Rest;

/// <summary>
///     Base of the Administration controllers. The organization and the user always come from the Iam JWT
///     (claims <c>organizationId</c> and <c>NameIdentifier</c>), never from the request body (AC-10).
/// </summary>
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
public abstract class AdministrationControllerBase(ProblemDetailsFactory problemDetailsFactory) : ControllerBase
{
    protected ProblemDetailsFactory ProblemDetails => problemDetailsFactory;

    /// <summary>Organization of the authenticated user (the Iam policies guarantee the claim exists).</summary>
    protected Guid OrganizationId => User.GetOrganizationId() ?? Guid.Empty;

    /// <summary>Iam user id of the authenticated user.</summary>
    protected Guid CurrentUserId => User.GetUserId() ?? Guid.Empty;
}
