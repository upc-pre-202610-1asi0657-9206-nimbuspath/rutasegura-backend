using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace Monolith.Shared.Interfaces.Security;
/// <summary>Service authentication, independent of family JWTs. Missing secrets fail closed.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class InternalDirectoryTokenAttribute : Attribute, IAsyncAuthorizationFilter
{
    public bool TripTrackingOnly { get; set; }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var keys = new[] { TripTrackingOnly ? null : config["InternalServices:NotificationToken"], config["InternalServices:TripTrackingToken"] }
            .Where(k => k?.Length >= 32).ToArray();
        var token = context.HttpContext.Request.Headers["X-Service-Token"].ToString();
        if (keys.Length == 0) context.Result = new ObjectResult(new { code = "DIRECTORY_NOT_CONFIGURED" }) { StatusCode = 503 };
        else if (token.Length > 512 || !keys.Any(k => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(k!)), SHA256.HashData(Encoding.UTF8.GetBytes(token))))) context.Result = new UnauthorizedResult();
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    }
}
