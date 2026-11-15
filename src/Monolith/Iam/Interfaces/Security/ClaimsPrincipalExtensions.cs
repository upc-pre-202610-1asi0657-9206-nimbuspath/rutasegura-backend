using System.Security.Claims;

namespace Monolith.Iam.Interfaces.Security;

/// <summary>Reads the identity Iam put in the JWT. The organization is never taken from the request body.</summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(IamClaimTypes.UserId), out var id) && id != Guid.Empty ? id : null;

    public static Guid? GetOrganizationId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(IamClaimTypes.OrganizationId), out var id) && id != Guid.Empty
            ? id
            : null;

    public static string? GetRole(this ClaimsPrincipal principal) => principal.FindFirstValue(IamClaimTypes.Role);
}
