using System.Reflection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Monolith.Iam.Infrastructure.Authorization;

/// <summary>
///     Shows the <c>X-Provisioning-Key</c> header in Swagger for the endpoints protected with
///     <see cref="RequireProvisioningKeyAttribute" />, so organization provisioning can be tried from Swagger UI.
/// </summary>
public sealed class ProvisioningKeyHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var protectedEndpoint =
            context.MethodInfo.GetCustomAttributes<RequireProvisioningKeyAttribute>(true).Any() ||
            (context.MethodInfo.DeclaringType?.GetCustomAttributes<RequireProvisioningKeyAttribute>(true).Any() ?? false);
        if (!protectedEndpoint) return;

        operation.Parameters ??= new List<IOpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = IamProvisioningOptions.HeaderName,
            In = ParameterLocation.Header,
            Required = true,
            Description = "Platform key (configuration Iam:Provisioning:ApiKey).",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });
    }
}
