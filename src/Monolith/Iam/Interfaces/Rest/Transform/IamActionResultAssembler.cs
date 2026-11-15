using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Domain.Model;
using Monolith.Shared.Application.Model;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;

namespace Monolith.Iam.Interfaces.Rest.Transform;

public static class IamActionResultAssembler
{
    public static int ToStatusCode(IamError error) => error switch
    {
        IamError.UserNotFound or IamError.OrganizationNotFound => StatusCodes.Status404NotFound,
        IamError.EmailAlreadyRegistered => StatusCodes.Status409Conflict,
        IamError.InvalidCredentials => StatusCodes.Status401Unauthorized,
        IamError.SelfRegistrationNotAllowed or IamError.OrganizationNotActive => StatusCodes.Status403Forbidden,
        IamError.InvalidIamData or IamError.WeakPassword => StatusCodes.Status400BadRequest,
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

    public static IActionResult Problem(ControllerBase controller, ProblemDetailsFactory problemDetailsFactory,
        IamError error, string message) =>
        problemDetailsFactory.CreateProblemDetails(controller, ToStatusCode(error), error, message);
}
