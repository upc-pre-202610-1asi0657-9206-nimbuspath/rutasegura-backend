using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Domain.Model;
using Monolith.Shared.Application.Model;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;

namespace Monolith.Iam.Interfaces.Rest.Transform;

public static class IamActionResultAssembler
{
    private static int ToStatusCode(IamError error) => error switch
    {
        IamError.UserNotFound or IamError.OrganizationNotFound => StatusCodes.Status404NotFound,
        IamError.EmailAlreadyRegistered => StatusCodes.Status409Conflict,
        IamError.InvalidCredentials or IamError.InvalidIamData => StatusCodes.Status400BadRequest,
        IamError.OperationCancelled => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };

    public static IActionResult ToActionResult<T>(
        ControllerBase controller,
        Result<T> result,
        ProblemDetailsFactory problemDetailsFactory,
        Func<T, IActionResult> successAction)
    {
        if (result.IsSuccess) return successAction(result.Value!);
        return problemDetailsFactory.CreateProblemDetails(controller,
            ToStatusCode((IamError)result.Error!), result.Error, result.Message);
    }
}