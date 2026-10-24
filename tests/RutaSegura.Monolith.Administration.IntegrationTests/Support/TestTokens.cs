using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Monolith.Iam.Interfaces.Security;

namespace RutaSegura.Monolith.Administration.IntegrationTests.Support;

/// <summary>Builds JWTs the same way Iam's TokenService does, to simulate stale, forged or expired tokens.</summary>
public static class TestTokens
{
    public static string Create(string secret, Guid userId, string role, Guid? organizationId,
        DateTime? notBefore = null, DateTime? expires = null, string? issuer = null, string? audience = null)
    {
        var claims = new List<Claim>
        {
            new(IamClaimTypes.UserId, userId.ToString()),
            new(IamClaimTypes.Role, role)
        };
        if (organizationId is not null) claims.Add(new Claim(IamClaimTypes.OrganizationId, organizationId.ToString()!));

        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = notBefore ?? now.AddMinutes(-1),
            IssuedAt = notBefore ?? now.AddMinutes(-1),
            Expires = expires ?? now.AddHours(1),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256Signature)
        });
    }
}
