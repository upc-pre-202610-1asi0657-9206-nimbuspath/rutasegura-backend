using Microsoft.AspNetCore.Mvc;
using RutaSegura.BuildingBlocks.Application.Results;

namespace RutaSegura.Notification.Interfaces.Rest.Controllers;

[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(500)]
[ProducesResponseType<ProblemDetails>(503)]
public abstract class NotificationControllerBase : ControllerBase
{
    protected IActionResult Failure(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Unauthorized => 401, ErrorType.Forbidden => 403, ErrorType.NotFound => 404,
            ErrorType.Conflict => 409, _ => 400
        };
        var details = new ProblemDetails { Status = status, Title = error.Message };
        details.Extensions["code"] = error.Code;
        return StatusCode(status, details);
    }
}
