using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Application.CommandServices;
using Monolith.Iam.Application.QueryServices;
using Monolith.Iam.Domain.Model;
using Monolith.Iam.Domain.Model.Commands;
using Monolith.Iam.Domain.Model.Queries;
using Monolith.Iam.Interfaces.Rest.Transform;
using Monolith.Iam.Interfaces.Rest.Resources;
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
    [HttpPost]
    [SwaggerOperation(Summary = "Sign up", Description = "Registers a new user account.", OperationId = "SignUp")]
    public async Task<IActionResult> SignUp(SignUpResource resource, CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new SignUpCommand(resource.FirstName, resource.LastName,
            resource.Email, resource.Password, resource.RoleTier, resource.OrganizationId), cancellationToken);
        return IamActionResultAssembler.ToActionResult(this, result, problemDetailsFactory,
            user => CreatedAtAction(nameof(GetUserById), new { userId = user.Id.Identifier },
                IamResourceFromEntityAssembler.ToResourceFromEntity(user)));
    }

    [HttpPost("sign-in")]
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

    [Authorize]
    [HttpGet("{userId:guid}")]
    [SwaggerOperation(Summary = "Get a user by id", OperationId = "GetUserById")]
    public async Task<IActionResult> GetUserById(Guid userId, CancellationToken cancellationToken)
    {
        var user = await queryService.Handle(new GetUserByIdQuery(userId), cancellationToken);
        if (user is null)
            return problemDetailsFactory.CreateProblemDetails(this, StatusCodes.Status404NotFound,
                IamError.UserNotFound, "User was not found.");
        return Ok(IamResourceFromEntityAssembler.ToResourceFromEntity(user));
    }

    [Authorize]
    [HttpGet]
    [SwaggerOperation(Summary = "Get all users", OperationId = "GetAllUsers")]
    public async Task<IActionResult> GetAllUsers(CancellationToken cancellationToken)
    {
        var users = await queryService.Handle(new GetAllUsersQuery(), cancellationToken);
        return Ok(users.Select(IamResourceFromEntityAssembler.ToResourceFromEntity));
    }
}