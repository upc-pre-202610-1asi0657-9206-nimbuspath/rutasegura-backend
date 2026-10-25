using RutaSegura.TripTracking.Api.Errors;

namespace RutaSegura.TripTracking.Api.Security;

/// <summary>Códigos estables (<c>extensions.code</c>) de las respuestas 401 / 403 / 503 de seguridad.</summary>
public static class SecurityErrorCodes
{
    /// <summary>401: no se envió token.</summary>
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>401: firma, emisor, audiencia o algoritmo no son los de Iam.</summary>
    public const string InvalidToken = "INVALID_TOKEN";

    /// <summary>401: el token de Iam venció (8 h); hay que volver a iniciar sesión.</summary>
    public const string TokenExpired = "TOKEN_EXPIRED";

    /// <summary>403: el rol o la organización del token no permiten la operación.</summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>403: Iam ya no reconoce la cuenta (eliminada, otro rol u organización, organización suspendida).</summary>
    public const string IamAccountRejected = "IAM_ACCOUNT_REJECTED";

    /// <summary>503: Iam no respondió y <c>Iam:AccountValidation:WhenUnavailable=Deny</c>.</summary>
    public const string IamUnavailable = "IAM_UNAVAILABLE";
}

/// <summary>Respuestas RFC 9457 para el pipeline de autenticación/autorización (mismo formato que los demás errores).</summary>
internal static class SecurityProblems
{
    public const string FailureCodeKey = "trip-tracking:security-failure";

    public static Task WriteForbiddenAsync(HttpContext context)
    {
        var code = context.Items.TryGetValue(FailureCodeKey, out var value) && value is string failure
            ? failure
            : SecurityErrorCodes.Forbidden;

        return code switch
        {
            SecurityErrorCodes.IamUnavailable => WriteAsync(context, StatusCodes.Status503ServiceUnavailable, code,
                "No se pudo verificar la cuenta con Iam. Intenta nuevamente en unos segundos."),
            SecurityErrorCodes.IamAccountRejected => WriteAsync(context, StatusCodes.Status403Forbidden, code,
                "La cuenta ya no está vigente en Iam. Vuelve a iniciar sesión."),
            _ => WriteAsync(context, StatusCodes.Status403Forbidden, code,
                "La cuenta no tiene permiso para realizar esta operación.")
        };
    }

    public static Task WriteAsync(HttpContext context, int status, string code, string detail)
    {
        if (context.Response.HasStarted) return Task.CompletedTask;
        context.Response.StatusCode = status;
        if (status == StatusCodes.Status503ServiceUnavailable) context.Response.Headers.RetryAfter = "5";
        return context.Response.WriteAsJsonAsync(Problems.Create(context, status, code, detail), options: null,
            contentType: "application/problem+json");
    }
}
