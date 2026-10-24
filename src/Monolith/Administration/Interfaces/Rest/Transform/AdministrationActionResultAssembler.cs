using Microsoft.AspNetCore.Mvc;
using Monolith.Administration.Domain.Model;
using Monolith.Shared.Application.Model;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;

namespace Monolith.Administration.Interfaces.Rest.Transform;

/// <summary>Maps <see cref="AdministrationError" /> to HTTP status codes and RFC 7807 problem details.</summary>
public static class AdministrationActionResultAssembler
{
    public static int ToStatusCode(AdministrationError error) => error switch
    {
        AdministrationError.DriverNotFound or
            AdministrationError.VehicleNotFound or
            AdministrationError.RouteNotFound or
            AdministrationError.RouteStopNotFound or
            AdministrationError.StudentNotOnRoute or
            AdministrationError.AlertNotFound => StatusCodes.Status404NotFound,

        AdministrationError.InvalidAdministrationData => StatusCodes.Status400BadRequest,

        AdministrationError.DriverAccountNotValid or
            AdministrationError.DriverLicenseExpired or
            AdministrationError.ResourceBelongsToAnotherOrganization => StatusCodes.Status422UnprocessableEntity,

        AdministrationError.DatabaseError or
            AdministrationError.InternalServerError or
            AdministrationError.None => StatusCodes.Status500InternalServerError,

        // Duplicates, invalid transitions, schedule conflicts, aforo, plan limit, …: the request conflicts with
        // the current state of the resource.
        _ => StatusCodes.Status409Conflict
    };

    public static IActionResult ToActionResult<T>(
        ControllerBase controller,
        Result<T> result,
        ProblemDetailsFactory problemDetailsFactory,
        Func<T, IActionResult> successAction)
    {
        if (result.IsSuccess) return successAction(result.Value!);
        var error = result.Error as AdministrationError? ?? AdministrationError.InternalServerError;
        return problemDetailsFactory.CreateProblemDetails(controller, ToStatusCode(error), error, result.Message);
    }

    public static IActionResult NotFound(ControllerBase controller, ProblemDetailsFactory problemDetailsFactory,
        AdministrationError error, string message) =>
        problemDetailsFactory.CreateProblemDetails(controller, StatusCodes.Status404NotFound, error, message);
}
