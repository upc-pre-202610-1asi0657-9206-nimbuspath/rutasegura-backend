using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using RutaSegura.TripTracking.Application.Common;

namespace RutaSegura.TripTracking.Api.Security;

/// <summary>
/// Validación del JWT emitido por IAM (PT-10, Resource Server). Debe usar el MISMO secreto que
/// <c>TokenSettings:Secret</c> de IAM (HMAC-SHA256). Issuer/Audience se validan solo si se configuran.
/// </summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required, MinLength(32, ErrorMessage = "Jwt:Secret debe tener al menos 32 caracteres (HS256).")]
    public string Secret { get; set; } = string.Empty;

    public string? Issuer { get; set; }
    public string? Audience { get; set; }
}

/// <summary>Roles emitidos por IAM (<c>RoleTier</c>): ADMIN, DRIVER, PARENT.</summary>
public static class Roles
{
    public const string Admin = "ADMIN";
    public const string Driver = "DRIVER";
    public const string Parent = "PARENT";
}

public static class Policies
{
    public const string Driver = "driver";
    public const string Guardian = "guardian";
    public const string Admin = "admin";
    public const string Staff = "staff";
    public const string TripViewer = "trip-viewer";
}

public static class Claims
{
    public const string OrganizationId = "organizationId";
}

internal static class SecurityExtensions
{
    public const string HubPath = "/hubs/trip-tracking";

    public static IServiceCollection AddTripTrackingSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret.PadRight(32))),
                    ValidateIssuer = !string.IsNullOrWhiteSpace(jwt.Issuer),
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = !string.IsNullOrWhiteSpace(jwt.Audience),
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = ClaimTypes.NameIdentifier,
                    RoleClaimType = ClaimTypes.Role
                };

                // SignalR envía el token por query string (los WebSockets del navegador no admiten cabeceras).
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(HubPath))
                            context.Token = token;
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        // IAM usa ClaimTypes.Role; otros emisores usan "role". Se normaliza para [Authorize(Roles)].
                        if (context.Principal?.Identity is ClaimsIdentity identity && !identity.HasClaim(c => c.Type == ClaimTypes.Role))
                        {
                            foreach (var role in identity.FindAll("role").ToList())
                                identity.AddClaim(new Claim(ClaimTypes.Role, role.Value.ToUpperInvariant()));
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Driver, p => p.RequireAuthenticatedUser().RequireRole(Roles.Driver))
            .AddPolicy(Policies.Guardian, p => p.RequireAuthenticatedUser().RequireRole(Roles.Parent))
            .AddPolicy(Policies.Admin, p => p.RequireAuthenticatedUser().RequireRole(Roles.Admin))
            .AddPolicy(Policies.Staff, p => p.RequireAuthenticatedUser().RequireRole(Roles.Driver, Roles.Admin))
            .AddPolicy(Policies.TripViewer, p => p.RequireAuthenticatedUser().RequireRole(Roles.Driver, Roles.Admin, Roles.Parent));

        return services;
    }
}

/// <summary>Traduce los claims del token al <see cref="Requester"/> que entienden los casos de uso.</summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub") ?? user.FindFirstValue("nameid");
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    public static Guid? OrganizationId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(Claims.OrganizationId), out var id) && id != Guid.Empty ? id : null;

    public static Requester ToRequester(this ClaimsPrincipal user)
    {
        var role = user.IsInRole(Roles.Admin) ? RequesterRole.Admin
            : user.IsInRole(Roles.Driver) ? RequesterRole.Driver
            : RequesterRole.Guardian;
        return new Requester(user.UserId(), role, user.OrganizationId());
    }
}
