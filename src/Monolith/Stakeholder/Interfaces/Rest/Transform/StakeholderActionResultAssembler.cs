using Microsoft.AspNetCore.Mvc;
using Monolith.Shared.Application.Model;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Monolith.Stakeholder.Domain.Model;

namespace Monolith.Stakeholder.Interfaces.Rest.Transform;

/// <summary>Maps <see cref="StakeholderError" /> to HTTP status codes and RFC 7807 problem details (with <c>code</c>).</summary>
public static class StakeholderActionResultAssembler
{
    public static int ToStatusCode(StakeholderError error) => error switch
    {
        StakeholderError.StudentNotFound or
            StakeholderError.EnrollmentNotFound or
            StakeholderError.EnrollmentCodeNotFound or
            StakeholderError.RouteNotFound or
            StakeholderError.StudentPhotoNotFound => StatusCodes.Status404NotFound,

        StakeholderError.RouteNotOperatedByDriver or
            StakeholderError.StudentAccessDenied => StatusCodes.Status403Forbidden,

        StakeholderError.InvalidStakeholderData => StatusCodes.Status400BadRequest,
        StakeholderError.StudentPhotoInvalid => StatusCodes.Status422UnprocessableEntity,
        StakeholderError.RoutePlanningUnavailable => StatusCodes.Status503ServiceUnavailable,

        StakeholderError.InternalServerError or
            StakeholderError.None => StatusCodes.Status500InternalServerError,

        // Limits, duplicates, full routes, ended enrollments…: the request conflicts with the current state.
        _ => StatusCodes.Status409Conflict
    };

    public static IActionResult ToActionResult<T>(ControllerBase controller, Result<T> result,
        ProblemDetailsFactory problemDetailsFactory, Func<T, IActionResult> successAction)
    {
        if (result.IsSuccess) return successAction(result.Value!);
        var error = result.Error as StakeholderError? ?? StakeholderError.InternalServerError;
        return problemDetailsFactory.CreateProblemDetails(controller, ToStatusCode(error), error, result.Message);
    }

    public static IActionResult Problem(ControllerBase controller, ProblemDetailsFactory problemDetailsFactory,
        StakeholderError error, string message) =>
        problemDetailsFactory.CreateProblemDetails(controller, ToStatusCode(error), error, message);
}
