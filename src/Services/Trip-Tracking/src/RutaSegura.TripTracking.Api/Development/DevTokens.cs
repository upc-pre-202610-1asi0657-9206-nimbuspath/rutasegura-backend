using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RutaSegura.TripTracking.Api.Security;
using RutaSegura.TripTracking.Infrastructure.Seeding;

namespace RutaSegura.TripTracking.Api.Development;

/// <summary>
/// SOLO DESARROLLO (<c>DevTokens:Enabled=true</c> y entorno Development): emite tokens con el mismo formato que IAM
/// (<c>TokenService</c> del monolito) para probar con Postman / .http mientras IAM no está desplegado.
/// Nunca se registra en producción.
/// </summary>
internal static class DevTokens
{
    public sealed record DevTokenRequest(string Role, Guid? UserId = null, Guid? OrganizationId = null, string? Name = null);

    public static void MapDevTokens(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/dev").WithTags("Development").AllowAnonymous();

        group.MapPost("/tokens", (DevTokenRequest request, IOptions<JwtOptions> jwt, TimeProvider clock) =>
        {
            var role = request.Role.Trim().ToUpperInvariant();
            if (role is "GUARDIAN") role = Roles.Parent;
            if (role is not (Roles.Admin or Roles.Driver or Roles.Parent))
                return Results.Problem(statusCode: 400, detail: "role debe ser ADMIN, DRIVER o PARENT.");

            var userId = request.UserId ?? role switch
            {
                Roles.Driver => DemoData.DriverId,
                Roles.Parent => DemoData.GuardianOfValeria,
                _ => DemoData.AdminId
            };
            var organizationId = request.OrganizationId ?? DemoData.OrganizationId;

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Name, request.Name ?? $"Demo {role}"),
                new(ClaimTypes.Role, role),
                new(Claims.OrganizationId, organizationId.ToString())
            };

            var now = clock.GetUtcNow().UtcDateTime;
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                IssuedAt = now,
                NotBefore = now,
                Expires = now.AddHours(12),
                Issuer = jwt.Value.Issuer,
                Audience = jwt.Value.Audience,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.Secret)), SecurityAlgorithms.HmacSha256Signature)
            });

            return Results.Ok(new { accessToken = token, userId, role, organizationId, expiresAt = now.AddHours(12) });
        });

        group.MapGet("/demo-data", () => Results.Ok(new
        {
            organizationId = DemoData.OrganizationId,
            driverId = DemoData.DriverId,
            guardianId = DemoData.GuardianOfValeria,
            adminId = DemoData.AdminId,
            pickupRouteId = DemoData.PickupRouteId,
            returnRouteId = DemoData.ReturnRouteId,
            students = new { valeria = DemoData.Valeria, mateo = DemoData.Mateo, camila = DemoData.Camila },
            stops = new { stop1 = DemoData.Stop1, stop2 = DemoData.Stop2, school = DemoData.School }
        }));
    }
}
