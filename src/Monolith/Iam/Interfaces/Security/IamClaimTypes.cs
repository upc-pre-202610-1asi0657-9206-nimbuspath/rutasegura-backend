using System.Security.Claims;

namespace Monolith.Iam.Interfaces.Security;

/// <summary>Claims Iam writes in its JWT (see <c>TokenService</c>), published so other contexts read them the same way.</summary>
public static class IamClaimTypes
{
    public const string UserId = ClaimTypes.NameIdentifier;
    public const string Role = ClaimTypes.Role;
    public const string Email = ClaimTypes.Email;
    public const string Name = ClaimTypes.Name;
    public const string OrganizationId = "organizationId";
}
