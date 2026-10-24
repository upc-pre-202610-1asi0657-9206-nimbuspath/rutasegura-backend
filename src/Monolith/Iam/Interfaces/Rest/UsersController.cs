using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Application.CommandServices;
using Monolith.Iam.Application.QueryServices;
using Monolith.Iam.Domain.Model;
using Monolith.Iam.Domain.Model.Commands;
using Monolith.Iam.Domain.Model.Queries;
using Monolith.Iam.Domain.Model.ValueObjects;
using Monolith.Iam.Interfaces.Rest.Resources;
using Monolith.Iam.Interfaces.Rest.Transform;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Swashbuckle.AspNetCore.Annotations;

namespace Monolith.Iam.Interfaces.Rest;

[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Iam user accounts and authentication endpoints.")]
public class UsersController(
    IIamCommandService commandService,
    IIamQueryService queryService,
    ProblemDetailsFactory problemDetailsFactory) : ControllerBase
{
    /// <summary>Parents register themselves from the app. Administrators and drivers cannot self-register.</summary>
    [HttpPost]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Sign up (parents)", Description = "Registers a PARENT account.",
        OperationId = "SignUp")]
    public async Task<IActionResult> SignUp(SignUpResource resource, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(resource.RoleTier) &&
            !string.Equals(resource.RoleTier.Trim(), RoleTier.Parent, StringComparison.OrdinalIgnoreCase))
            return IamActionResultAssembler.Problem(this, problemDetailsFactory, IamError.SelfRegistrationNotAllowed,
                "Only parent accounts can be created by sign-up. Driver accounts are created by the administrator " +
                "of the organization, and the administrator is created with the subscription.");

        var result = await commandService.Handle(new SignUpCommand(resource.FirstName, resource.LastName,
            resource.Email, resource.Password, resource.OrganizationId), cancellationToken);
        return IamActionResultAssembler.ToActionResult(this, result, problemDetailsFactory,
            user => CreatedAtAction(nameof(GetUserById), new { userId = user.Id.Identifier },
                IamResourceFromEntityAssembler.ToResourceFromEntity(user)));
    }

    [HttpPost("sign-in")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Sign in", Description = "Authenticates a user and issues a JWT.",
        OperationId = "SignIn")]
    public async Task<IActionResult> SignIn(SignInResource resource, CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new SignInCommand(resource.Email, resource.Password),
            cancellationToken);
        return IamActionResultAssembler.ToActionResult(this, result, problemDetailsFactory,
            authenticated => Ok(IamResourceFromEntityAssembler.ToAuthenticatedResourceFromEntity(
                authenticated.User, authenticated.Token)));
    }

    /// <summary>The administrator creates the accounts of the drivers of its own organization.</summary>
    [HttpPost("drivers")]
    [Authorize(Policy = IamPolicies.OrganizationAdministrator)]
    [SwaggerOperation(Summary = "Create a driver account in my organization", OperationId = "RegisterDriverAccount")]
    public async Task<IActionResult> RegisterDriverAccount(RegisterDriverAccountResource resource,
        CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new RegisterDriverAccountCommand(
            User.GetOrganizationId() ?? Guid.Empty, resource.FirstName, resource.LastName, resource.Email,
            resource.Password), cancellationToken);
        return IamActionResultAssembler.ToActionResult(this, result, problemDetailsFactory,
            user => CreatedAtAction(nameof(GetUserById), new { userId = user.Id.Identifier },
                IamResourceFromEntityAssembler.ToResourceFromEntity(user)));
    }

    [HttpGet("me")]
    [Authorize(Policy = IamPolicies.Account)]
    [SwaggerOperation(Summary = "My account", OperationId = "GetMyAccount")]
    public async Task<IActionResult> GetMyAccount(CancellationToken cancellationToken)
    {
        var user = await queryService.Handle(new GetUserByIdQuery(User.GetUserId() ?? Guid.Empty), cancellationToken);
        return user is null ? UserNotFound() : Ok(IamResourceFromEntityAssembler.ToResourceFromEntity(user));
    }

    /// <summary>A user can read its own account; an administrator, the accounts of its organization.</summary>
    [HttpGet("{userId:guid}")]
    [Authorize(Policy = IamPolicies.Account)]
    [SwaggerOperation(Summary = "Get a user by id", OperationId = "GetUserById")]
    public async Task<IActionResult> GetUserById(Guid userId, CancellationToken cancellationToken)
    {
        var user = await queryService.Handle(new GetUserByIdQuery(userId), cancellationToken);
        var requesterId = User.GetUserId();
        var isSelf = user is not null && user.Id.Identifier == requesterId;
        var isAdministratorOfTheUser = user?.OrganizationId is not null &&
                                       User.IsInRole(RoleTier.Admin) &&
                                       user.OrganizationId.Identifier == User.GetOrganizationId();
        return isSelf || isAdministratorOfTheUser
            ? Ok(IamResourceFromEntityAssembler.ToResourceFromEntity(user!))
            : UserNotFound();
    }

    /// <summary>Accounts of the administrator's organization only.</summary>
    [HttpGet]
    [Authorize(Policy = IamPolicies.OrganizationAdministrator)]
    [SwaggerOperation(Summary = "Accounts of my organization", OperationId = "GetUsersOfMyOrganization")]
    public async Task<IActionResult> GetUsersOfMyOrganization(CancellationToken cancellationToken)
    {
        var users = await queryService.Handle(new GetUsersByOrganizationQuery(User.GetOrganizationId() ?? Guid.Empty),
            cancellationToken);
        return Ok(users.Select(IamResourceFromEntityAssembler.ToResourceFromEntity));
    }

    private IActionResult UserNotFound() =>
        IamActionResultAssembler.Problem(this, problemDetailsFactory, IamError.UserNotFound, "User was not found.");
}
