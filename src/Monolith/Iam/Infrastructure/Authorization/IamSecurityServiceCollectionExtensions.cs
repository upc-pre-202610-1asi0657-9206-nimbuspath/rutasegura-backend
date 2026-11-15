using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Monolith.Iam.Infrastructure.Tokens.Jwt.Configuration;
using Monolith.Iam.Interfaces.ACL;
using Monolith.Iam.Interfaces.Security;

namespace Monolith.Iam.Infrastructure.Authorization;

/// <summary>
///     Security Iam provides to every bounded context of the monolith: JWT bearer validation of the tokens Iam
///     issues, role/organization policies (<see cref="IamPolicies" />) and RFC 7807 bodies for 401 and 403.
/// </summary>
public static class IamSecurityServiceCollectionExtensions
{
    public static IServiceCollection AddIamSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TokenSettings>(configuration.GetSection("TokenSettings"));
        services.Configure<IamProvisioningOptions>(configuration.GetSection(IamProvisioningOptions.Section));

        var tokenSecret = configuration["TokenSettings:Secret"];
        if (string.IsNullOrWhiteSpace(tokenSecret) || tokenSecret.Length < TokenSettings.MinimumSecretLength)
            throw new InvalidOperationException(
                $"TokenSettings:Secret must be configured (at least {TokenSettings.MinimumSecretLength} characters). " +
                "Use appsettings.Development.json locally and the TokenSettings__Secret environment variable or a " +
                "secret store elsewhere; it must never be committed to appsettings.json.");
        var issuer = configuration["TokenSettings:Issuer"];
        var audience = configuration["TokenSettings:Audience"];

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Claim types are read exactly as TokenService writes them (see IamClaimTypes).
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenSecret)),
                    ValidateIssuer = !string.IsNullOrWhiteSpace(issuer),
                    ValidIssuer = issuer,
                    ValidateAudience = !string.IsNullOrWhiteSpace(audience),
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = ClaimTypes.NameIdentifier,
                    RoleClaimType = ClaimTypes.Role
                };
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        await WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                            "Unauthorized", "A valid access token issued by Iam is required.");
                    },
                    OnForbidden = context => WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden,
                        "Forbidden", "The account is not allowed to perform this operation.")
                };
            });

        services.AddScoped<IAuthorizationHandler, ActiveIamAccountHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(IamPolicies.Account, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new ActiveIamAccountRequirement(null, false)));
            options.AddPolicy(IamPolicies.OrganizationAdministrator, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(IamRoles.Admin)
                .RequireClaim(IamClaimTypes.OrganizationId)
                .AddRequirements(new ActiveIamAccountRequirement(IamRoles.Admin, true)));
            options.AddPolicy(IamPolicies.OrganizationDriver, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(IamRoles.Driver)
                .RequireClaim(IamClaimTypes.OrganizationId)
                .AddRequirements(new ActiveIamAccountRequirement(IamRoles.Driver, true)));
            options.AddPolicy(IamPolicies.Guardian, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(IamRoles.Parent)
                .AddRequirements(new ActiveIamAccountRequirement(IamRoles.Parent, false)));
            options.AddPolicy(IamPolicies.OrganizationStaff, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(IamRoles.Admin, IamRoles.Driver)
                .RequireClaim(IamClaimTypes.OrganizationId)
                .AddRequirements(new ActiveIamAccountRequirement(null, true)));
        });

        return services;
    }

    private static Task WriteProblemAsync(HttpContext context, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        }, options: null, contentType: "application/problem+json");
    }
}
