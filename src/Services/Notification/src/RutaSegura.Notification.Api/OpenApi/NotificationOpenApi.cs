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
            "ProviderAccepted no acredita recepcion en dispositivo ni lectura. La bandeja usa offset/limit sin total; avanzar offset por el limite solicitado.";
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
        foreach (var path in document.Paths.Where(p => p.Key.StartsWith("/api/v1/", StringComparison.Ordinal)))
            foreach (var operation in path.Value.Operations?.Values.AsEnumerable() ?? Enumerable.Empty<OpenApiOperation>())
            {
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
                operation.Description = path.Key.Contains("announcements", StringComparison.Ordinal)
                    ? "Solo ADMIN vigente y operador autorizado. requestId idempotente: 201 nuevo, 200 repeticion identica, 409 contenido distinto."
                    : path.Key.Contains("push-subscriptions", StringComparison.Ordinal)
                        ? "Solo suscripciones propias de la organizacion autorizada. La respuesta no contiene el token push."
                        : "Solo avisos propios de la organizacion y con vinculo vigente. Avisos ajenos o revocados se ocultan con 404.";
            }
        return Task.CompletedTask;
    }
}
