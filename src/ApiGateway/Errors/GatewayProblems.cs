using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace RutaSegura.ApiGateway.Errors;

/// <summary>Códigos estables (<c>extensions.code</c>) de los errores que genera el propio Gateway.</summary>
public static class GatewayErrorCodes
{
    /// <summary>404: ninguna ruta pública coincide (incluye <c>/internal/**</c>, <c>/dev/**</c> y swagger de los servicios).</summary>
    public const string RouteNotFound = "GATEWAY_ROUTE_NOT_FOUND";

    /// <summary>502: el servicio de destino no respondió (caído, reiniciando, conexión rechazada).</summary>
    public const string UpstreamUnavailable = "UPSTREAM_UNAVAILABLE";

    /// <summary>504: el servicio de destino tardó demasiado.</summary>
    public const string UpstreamTimeout = "UPSTREAM_TIMEOUT";

    /// <summary>503: el servicio existe pero está deshabilitado en este despliegue (p. ej. Notification).</summary>
    public const string ServiceDisabled = "SERVICE_DISABLED";

    /// <summary>429: demasiados intentos (p. ej. inicio de sesión).</summary>
    public const string TooManyRequests = "TOO_MANY_REQUESTS";

    /// <summary>413: cuerpo más grande que <c>Gateway:MaxRequestBodyBytes</c>.</summary>
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
}

/// <summary>Mismo formato RFC 9457 que los servicios (ProblemDetails + <c>code</c> + <c>traceId</c>).</summary>
public static class GatewayProblems
{
    public static Task WriteAsync(HttpContext context, int status, string code, string detail)
    {
        if (context.Response.HasStarted) return Task.CompletedTask;
        context.Response.Clear();
        context.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = Title(status),
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }

    private static string Title(int status) => status switch
    {
        404 => "No encontrado",
        413 => "Solicitud demasiado grande",
        429 => "Demasiadas solicitudes",
        502 => "Servicio no disponible",
        503 => "Servicio deshabilitado",
        504 => "Tiempo de espera agotado",
        _ => "Error del Gateway"
    };
}
