using RutaSegura.ApiGateway.Errors;
using Yarp.ReverseProxy.Forwarder;

namespace RutaSegura.ApiGateway.Proxy;

internal static class ProxyPipeline
{
    /// <summary>
    /// Antes de reenviar: rutas de servicios deshabilitados → 503. Después: si YARP no pudo hablar con el servicio,
    /// responde ProblemDetails (502/504) con <c>code</c>, en vez de un 502 vacío, para que la app decida por código.
    /// </summary>
    public static IApplicationBuilder UseGatewayErrors(this IApplicationBuilder proxy) => proxy.Use(async (context, next) =>
    {
        var route = context.GetReverseProxyFeature().Route.Config;
        if (route.Metadata is { } metadata && metadata.ContainsKey(GatewayNames.DisabledMetadataKey))
        {
            await GatewayProblems.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, GatewayErrorCodes.ServiceDisabled,
                "Este servicio no está habilitado en este entorno.");
            return;
        }

        await next();

        var error = context.GetForwarderErrorFeature();
        if (error is null || context.Response.HasStarted) return;

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RutaSegura.ApiGateway.Proxy");
        logger.LogWarning(error.Exception, "Falló el reenvío de {Method} {Path} a {Cluster}: {Error}",
            context.Request.Method, context.Request.Path, route.ClusterId, error.Error);

        if (error.Exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
            await GatewayProblems.WriteAsync(context, StatusCodes.Status413PayloadTooLarge, GatewayErrorCodes.PayloadTooLarge,
                "El cuerpo de la solicitud supera el tamaño permitido.");
        else if (error.Error == ForwarderError.RequestTimedOut)
            await GatewayProblems.WriteAsync(context, StatusCodes.Status504GatewayTimeout, GatewayErrorCodes.UpstreamTimeout,
                "El servicio tardó demasiado en responder. Intenta nuevamente.");
        else if (error.Error is ForwarderError.RequestCanceled or ForwarderError.RequestBodyCanceled)
            return; // el cliente se fue: no hay a quién responder
        else
            await GatewayProblems.WriteAsync(context, StatusCodes.Status502BadGateway, GatewayErrorCodes.UpstreamUnavailable,
                "El servicio no está disponible en este momento. Intenta nuevamente en unos segundos.");
    });
}
