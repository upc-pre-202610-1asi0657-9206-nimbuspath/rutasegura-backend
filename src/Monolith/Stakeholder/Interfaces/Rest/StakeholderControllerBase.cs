using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;

namespace Monolith.Stakeholder.Interfaces.Rest;

/// <summary>
///     Base of the Stakeholder controllers. The user, role and organization always come from the Iam JWT, never from
///     the request body.
/// </summary>
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
public abstract class StakeholderControllerBase(ProblemDetailsFactory problemDetailsFactory) : ControllerBase
{
    protected ProblemDetailsFactory ProblemDetails => problemDetailsFactory;

    protected Guid CurrentUserId => User.GetUserId() ?? Guid.Empty;

    protected string CurrentRole => User.GetRole() ?? string.Empty;

    protected Guid OrganizationId => User.GetOrganizationId() ?? Guid.Empty;
}
