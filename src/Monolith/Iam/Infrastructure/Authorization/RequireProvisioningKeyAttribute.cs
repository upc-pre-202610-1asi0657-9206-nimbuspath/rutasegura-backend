using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Monolith.Iam.Infrastructure.Authorization;

/// <summary>
///     Protects platform-only endpoints (organization provisioning) with the <c>X-Provisioning-Key</c> header.
///     404 when provisioning is not configured, 401 without the header, 403 with a wrong key.
/// </summary>
public sealed class RequireProvisioningKeyAttribute() : TypeFilterAttribute(typeof(ProvisioningKeyFilter));

public sealed class ProvisioningKeyFilter(IOptions<IamProvisioningOptions> options, ILogger<ProvisioningKeyFilter> logger)
    : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var configuredKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(configuredKey) || configuredKey.Length < IamProvisioningOptions.MinimumKeyLength)
        {
            context.Result = Problem(context, StatusCodes.Status404NotFound, "Not Found",
                "Organization provisioning is not enabled in this environment.");
            return;
        }

        var provided = context.HttpContext.Request.Headers[IamProvisioningOptions.HeaderName].ToString();
        if (string.IsNullOrEmpty(provided))
        {
            context.Result = Problem(context, StatusCodes.Status401Unauthorized, "Unauthorized",
                $"The {IamProvisioningOptions.HeaderName} header is required.");
            return;
        }

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided),
                Encoding.UTF8.GetBytes(configuredKey)))
        {
            logger.LogWarning("Organization provisioning rejected: invalid key from {RemoteIp}",
                context.HttpContext.Connection.RemoteIpAddress);
            context.Result = Problem(context, StatusCodes.Status403Forbidden, "Forbidden",
                "The provisioning key is not valid.");
        }
    }

    private static ObjectResult Problem(AuthorizationFilterContext context, int status, string title, string detail) =>
        new(new ProblemDetails
        {
            Status = status, Title = title, Detail = detail, Instance = context.HttpContext.Request.Path
        }) { StatusCode = status, ContentTypes = { "application/problem+json" } };
}
