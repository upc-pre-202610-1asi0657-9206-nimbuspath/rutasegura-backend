using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RutaSegura.Notification.Api.OpenApi;

public sealed class NotificationOpenApi : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "RutaSegura - Notifications API";
        document.Info.Version = "v1";
        document.Info.Description = "Bandeja persistente, lectura, comunicados ADMIN y suscripciones push. JWT de IAM con organizationId autorizado; cuenta y vinculos vigentes. " +
            "SignalR /hubs/notifications emite NotificationAvailable { notificationId, createdAt }; recuperar contenido mediante GET autorizado. " +
            "ProviderAccepted no acredita recepcion en dispositivo ni lectura. Preferir /notifications/page con cursor firmado de 30 minutos; " +
            "seguir nextCursor mientras hasMore sea true, incluso con items vacios. El listado offset/limit conserva su contrato de array sin total.";
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
        foreach (var path in document.Paths.Where(p => p.Key.StartsWith("/api/v1/", StringComparison.Ordinal)))
            foreach (var operation in path.Value.Operations?.Values.AsEnumerable() ?? Enumerable.Empty<OpenApiOperation>())
            {
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
                operation.Description = path.Key.Contains("notifications/deliveries", StringComparison.Ordinal)
                    ? "Solo ADMIN vigente y operador autorizado de la organizacion. Lista metadatos de entregas Failed, sin tokens ni contenido. " +
                      "Replay revalida destinatario, caducidad y suscripcion: 202 al programar, 200 repeticion identica de requestId, 409 conflicto o presupuesto agotado. " +
                      "reasonCode: ProviderRecovered, ConfigurationFixed o ManualReview. No envia durante la peticion ni crea otro aviso."
                    : path.Key.EndsWith("notifications/page", StringComparison.Ordinal)
                        ? "Bandeja propia autorizada por vinculo vigente. Respuesta { items, nextCursor, hasMore }; limit 1-100. " +
                          "Cursor ligado al usuario, organizacion y unreadOnly, valido 30 minutos: invalido/expirado devuelve 400. " +
                          "Mantener filtros y seguir nextCursor incluso si items esta vacio; no se devuelve total. Reiniciar sin cursor para refrescar."
                    : path.Key.Contains("announcements", StringComparison.Ordinal)
                    ? "Solo ADMIN vigente y operador autorizado. requestId idempotente: 201 nuevo, 200 repeticion identica, 409 contenido distinto."
                    : path.Key.Contains("push-subscriptions", StringComparison.Ordinal)
                        ? "Solo suscripciones propias de la organizacion autorizada. La respuesta no contiene el token push."
                        : "Solo avisos propios de la organizacion y con vinculo vigente. Avisos ajenos o revocados se ocultan con 404.";
            }
        return Task.CompletedTask;
    }
}
