using Microsoft.AspNetCore.Mvc;
using Monolith.Shared.Application.Model;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Monolith.Subscription.Domain.Model;

namespace Monolith.Subscription.Interfaces.Rest.Transform;

/// <summary>Maps <see cref="SubscriptionError" /> to HTTP status codes and RFC 7807 problem details (with <c>code</c>).</summary>
public static class SubscriptionActionResultAssembler
{
    public static int ToStatusCode(SubscriptionError error) => error switch
    {
        SubscriptionError.PlanNotFound or
            SubscriptionError.PlanChangeNotFound => StatusCodes.Status404NotFound,

        SubscriptionError.InvalidSubscriptionData => StatusCodes.Status400BadRequest,
        SubscriptionError.PaymentNotificationInvalid => StatusCodes.Status401Unauthorized,
        SubscriptionError.SimulatedPaymentsDisabled => StatusCodes.Status403Forbidden,
        SubscriptionError.PaymentGatewayUnavailable => StatusCodes.Status503ServiceUnavailable,

        SubscriptionError.InternalServerError or
            SubscriptionError.None => StatusCodes.Status500InternalServerError,

        // Plan already contracted, a change already pending, a final change, a concurrent save…
        _ => StatusCodes.Status409Conflict
    };

    public static IActionResult ToActionResult<T>(ControllerBase controller, Result<T> result,
        ProblemDetailsFactory problemDetailsFactory, Func<T, IActionResult> successAction)
    {
        if (result.IsSuccess) return successAction(result.Value!);
        var error = result.Error as SubscriptionError? ?? SubscriptionError.InternalServerError;
        return problemDetailsFactory.CreateProblemDetails(controller, ToStatusCode(error), error, result.Message);
    }

    public static IActionResult Problem(ControllerBase controller, ProblemDetailsFactory problemDetailsFactory,
        SubscriptionError error, string message) =>
        problemDetailsFactory.CreateProblemDetails(controller, ToStatusCode(error), error, message);
}
