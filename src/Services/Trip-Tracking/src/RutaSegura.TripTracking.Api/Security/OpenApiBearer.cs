using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RutaSegura.TripTracking.Api.Security;

/// <summary>Agrega el esquema Bearer (JWT de IAM) al documento OpenAPI para probar desde Swagger UI.</summary>
internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "RutaSegura — Trip & Tracking API";
        document.Info.Description = "Ejecución de viajes y seguimiento en vivo. Roles del JWT de IAM: DRIVER, PARENT, ADMIN.";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Token emitido por IAM (en desarrollo: POST /dev/tokens)."
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}
