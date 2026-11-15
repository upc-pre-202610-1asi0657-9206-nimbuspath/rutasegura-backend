using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using RutaSegura.TripTracking.Application.Common;

namespace RutaSegura.TripTracking.Api.Security;

/// <summary>
/// Validación del JWT que emite Iam (PT-10, Resource Server). Los valores deben ser los MISMOS que
/// <c>TokenSettings</c> del monolito: <c>Jwt:Secret</c> = <c>TokenSettings:Secret</c> (HMAC-SHA256),
/// <c>Jwt:Issuer</c> = <c>TokenSettings:Issuer</c> y <c>Jwt:Audience</c> = <c>TokenSettings:Audience</c>.
/// Los tres son obligatorios: Iam siempre emite <c>iss</c> y <c>aud</c>, y un token sin ellos no es de Iam.
/// </summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    /// <summary>Mínimo de bytes del secreto, igual que <c>TokenSettings.MinimumSecretLength</c> de Iam.</summary>
    public const int MinimumSecretLength = 32;

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}

/// <summary>
/// Roles que emite Iam (<c>RoleTier</c> / <c>IamRoles</c> del monolito). Copia del Published Language de Iam:
/// Trip &amp; Tracking es otro desplegable y no referencia al monolito (lo verifica la prueba de contrato).
/// </summary>
public static class Roles
{
    public const string Admin = "ADMIN";
    public const string Driver = "DRIVER";
    public const string Parent = "PARENT";
}

/// <summary>Claims que escribe el <c>TokenService</c> de Iam (<c>IamClaimTypes</c> del monolito).</summary>
public static class IamClaimTypes
{
    public const string UserId = ClaimTypes.NameIdentifier;
    public const string Role = ClaimTypes.Role;
    public const string Email = ClaimTypes.Email;
    public const string Name = ClaimTypes.Name;
    public const string OrganizationId = "organizationId";
}

/// <summary>Se mantiene por compatibilidad con el código existente; usa <see cref="IamClaimTypes"/>.</summary>
public static class Claims
{
    public const string OrganizationId = IamClaimTypes.OrganizationId;
}

/// <summary>
/// Políticas del servicio. Todas exigen un JWT válido de Iam y que la cuenta siga vigente en Iam
/// (<see cref="ActiveIamAccountRequirement"/>). Conductor y administrador, además, deben traer
/// <c>organizationId</c> en el token, igual que <c>IamPolicies.OrganizationDriver/OrganizationAdministrator</c>.
/// </summary>
public static class Policies
{
    public const string Driver = "driver";
    public const string Guardian = "guardian";
    public const string Admin = "admin";
    public const string Staff = "staff";
    public const string TripViewer = "trip-viewer";
}

internal static class SecurityExtensions
{
    public const string HubPath = "/hubs/trip-tracking";

    public static IServiceCollection AddTripTrackingSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        ValidateJwtOptions(jwt);
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.Section));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Los claims se leen tal como los escribe Iam (IamClaimTypes); el token se guarda para
                // poder preguntarle a Iam por la cuenta (ActiveIamAccountHandler).
                options.MapInboundClaims = false;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256, SecurityAlgorithms.HmacSha256Signature],
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.Zero, // igual que Iam
                    NameClaimType = IamClaimTypes.UserId,
                    RoleClaimType = IamClaimTypes.Role
                };

                options.Events = new JwtBearerEvents
                {
                    // SignalR envía el token por query string (los WebSockets del navegador no admiten cabeceras).
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(HubPath))
                            context.Token = token;
                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        var code = context.AuthenticateFailure switch
                        {
                            SecurityTokenExpiredException => SecurityErrorCodes.TokenExpired,
                            not null => SecurityErrorCodes.InvalidToken,
                            _ => SecurityErrorCodes.Unauthorized
                        };
                        await SecurityProblems.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, code,
                            "Se requiere un token de acceso válido emitido por Iam.");
                    },
                    OnForbidden = context => SecurityProblems.WriteForbiddenAsync(context.HttpContext)
                };
            });

        services.AddTripTrackingIamAccountValidation(configuration);

        services.AddAuthorizationBuilder()
            // [Authorize] sin política: cualquier cuenta vigente de Iam.
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new ActiveIamAccountRequirement(RequiresOrganization: false))
                .Build())
            .AddPolicy(Policies.Driver, p => p.RequireAuthenticatedUser().RequireRole(Roles.Driver)
                .RequireClaim(IamClaimTypes.OrganizationId)
                .AddRequirements(new ActiveIamAccountRequirement(RequiresOrganization: true)))
            .AddPolicy(Policies.Guardian, p => p.RequireAuthenticatedUser().RequireRole(Roles.Parent)
                .AddRequirements(new ActiveIamAccountRequirement(RequiresOrganization: false)))
            .AddPolicy(Policies.Admin, p => p.RequireAuthenticatedUser().RequireRole(Roles.Admin)
                .RequireClaim(IamClaimTypes.OrganizationId)
                .AddRequirements(new ActiveIamAccountRequirement(RequiresOrganization: true)))
            .AddPolicy(Policies.Staff, p => p.RequireAuthenticatedUser().RequireRole(Roles.Driver, Roles.Admin)
                .RequireClaim(IamClaimTypes.OrganizationId)
                .AddRequirements(new ActiveIamAccountRequirement(RequiresOrganization: true)))
            .AddPolicy(Policies.TripViewer, p => p.RequireAuthenticatedUser().RequireRole(Roles.Driver, Roles.Admin, Roles.Parent)
                .AddRequirements(new ActiveIamAccountRequirement(RequiresOrganization: false)));

        return services;
    }

    /// <summary>Falla al arrancar (como <c>AddIamSecurity</c>) en vez de aceptar tokens con una configuración débil.</summary>
    internal static void ValidateJwtOptions(JwtOptions jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt.Secret) || Encoding.UTF8.GetByteCount(jwt.Secret) < JwtOptions.MinimumSecretLength)
            throw new InvalidOperationException(
                $"Jwt:Secret debe configurarse con al menos {JwtOptions.MinimumSecretLength} bytes y ser el mismo " +
                "TokenSettings:Secret de Iam. Usa appsettings.Development.json en local y la variable Jwt__Secret o un " +
                "almacén de secretos en cualquier otro entorno.");
        if (string.IsNullOrWhiteSpace(jwt.Issuer) || string.IsNullOrWhiteSpace(jwt.Audience))
            throw new InvalidOperationException(
                "Jwt:Issuer y Jwt:Audience son obligatorios y deben ser los TokenSettings:Issuer/Audience de Iam " +
                "(por defecto rutasegura-iam / rutasegura-api).");
    }
}

/// <summary>Traduce los claims del token de Iam al <see cref="Requester"/> que entienden los casos de uso.</summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(IamClaimTypes.UserId), out var id) ? id : Guid.Empty;

    public static Guid? OrganizationId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(IamClaimTypes.OrganizationId), out var id) && id != Guid.Empty ? id : null;

    public static string? IamRole(this ClaimsPrincipal user) => user.FindFirstValue(IamClaimTypes.Role);

    /// <summary>
    /// Las políticas ya garantizan un rol de Iam; si llegara otro, el usuario queda vacío y el controller
    /// responde 401 antes de llegar a los casos de uso.
    /// </summary>
    public static Requester ToRequester(this ClaimsPrincipal user)
    {
        if (user.IsInRole(Roles.Admin)) return new Requester(user.UserId(), RequesterRole.Admin, user.OrganizationId());
        if (user.IsInRole(Roles.Driver)) return new Requester(user.UserId(), RequesterRole.Driver, user.OrganizationId());
        if (user.IsInRole(Roles.Parent)) return new Requester(user.UserId(), RequesterRole.Guardian, user.OrganizationId());
        return new Requester(Guid.Empty, RequesterRole.Guardian);
    }
}
