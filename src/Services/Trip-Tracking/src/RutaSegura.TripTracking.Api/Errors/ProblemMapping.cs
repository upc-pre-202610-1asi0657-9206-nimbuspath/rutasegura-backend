using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Application.Abstractions;

namespace RutaSegura.TripTracking.Api.Errors;

/// <summary>
/// Errores en formato RFC 9457 (ProblemDetails). El código estable viaja en <c>extensions.code</c>
/// (lo usa la app móvil para decidir qué mostrar) y todos los errores en <c>extensions.errors</c>.
/// </summary>
internal static class Problems
{
    public static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status422UnprocessableEntity
    };

    public static ProblemDetails Create(HttpContext http, int status, string code, string detail, IEnumerable<object>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = Title(status),
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
            Instance = http.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? http.TraceIdentifier;
        if (errors is not null) problem.Extensions["errors"] = errors.ToList();
        return problem;
    }

    public static ObjectResult ToProblem(this ControllerBase controller, IReadOnlyList<Error> errors)
    {
        var first = errors[0];
        var status = StatusFor(first.Type);
        var problem = Create(controller.HttpContext, status, first.Code, first.Message,
            errors.Select(e => new { code = e.Code, message = e.Message }));
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    public static ObjectResult ToProblemCode(this ControllerBase controller, Error error) => controller.ToProblem([error]);

    private static string Title(int status) => status switch
    {
        400 => "Solicitud inválida",
        401 => "No autenticado",
        403 => "Acceso denegado",
        404 => "No encontrado",
        409 => "Conflicto",
        422 => "Regla de negocio incumplida",
        429 => "Demasiadas solicitudes",
        503 => "Servicio no disponible",
        _ => "Error interno"
    };
}

/// <summary>
/// R-03 (5.1.4): las violaciones de reglas del dominio llegan como <see cref="DomainException"/> y se traducen aquí,
/// sin try/catch en controllers ni handlers.
/// </summary>
internal sealed class TripTrackingExceptionHandler(IProblemDetailsService problemDetails, ILogger<TripTrackingExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, detail) = exception switch
        {
            DomainException { Code: "TRIP_DRIVER_MISMATCH" } ex => (StatusCodes.Status403Forbidden, ex.Code, ex.Message),
            DomainException ex => (StatusCodes.Status422UnprocessableEntity, ex.Code, ex.Message),
            PersistenceConflictException ex => (StatusCodes.Status409Conflict, ex.Code, ex.Message),
            BadHttpRequestException ex => (ex.StatusCode, "BAD_REQUEST", ex.Message),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested =>
                (499, "REQUEST_CANCELLED", "La solicitud fue cancelada por el cliente."),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Ocurrió un error inesperado. Intenta nuevamente.")
        };

        if (status >= 500) logger.LogError(exception, "Error no controlado en {Path}", httpContext.Request.Path);
        else logger.LogInformation("{Path} → {Status} {Code}", httpContext.Request.Path, status, code);

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = Problems.Create(httpContext, status, code, detail)
        });
    }
}
