using System.Security.Claims;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Api.Security;

public static class NotificationIdentity
{
    public static Guid UserId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue("sub") ?? user.FindFirstValue("nameid"), out var id) ? id : Guid.Empty;
    public static Guid OrganizationId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue("organizationId"), out var id) ? id : Guid.Empty;
    public static string Group(Guid organization, Guid user) => $"notification:{organization:N}:{user:N}";
    public static bool Valid(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true && UserId(user) != Guid.Empty &&
        OrganizationId(user) != Guid.Empty && new[] { "PARENT", "ADMIN", "DRIVER" }.Any(user.IsInRole);
}

public sealed class HttpNotificationUserContext(IHttpContextAccessor accessor) : INotificationUserContext
{
    public NotificationUser? Current => accessor.HttpContext?.User is { } user && NotificationIdentity.Valid(user)
        ? new(NotificationIdentity.UserId(user), NotificationIdentity.OrganizationId(user), user.IsInRole("ADMIN")) : null;
}

public static class NotificationSecurity
{
    public static IServiceCollection AddNotificationSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"] ?? "";
        if (Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("Configura Jwt:Secret de al menos 32 bytes, compatible con IAM.");
        services.AddHttpContextAccessor();
        services.AddScoped<INotificationUserContext, HttpNotificationUserContext>();
        var iam = configuration.GetSection("Iam").Get<NotificationIamOptions>() ?? new();
        if (iam.AccountValidation.Enabled)
        {
            var uri = iam.Validate();
            if (string.IsNullOrWhiteSpace(configuration["Jwt:Issuer"]) || string.IsNullOrWhiteSpace(configuration["Jwt:Audience"]))
                throw new InvalidOperationException("Configura Jwt:Issuer y Jwt:Audience de IAM antes de habilitar la API integrada.");
            services.AddHttpClient<INotificationIamAccountVerifier, NotificationIamAccountVerifier>(NotificationIamAccountVerifier.ClientName, client =>
            {
                client.BaseAddress = uri;
                client.Timeout = iam.AccountValidation.Timeout;
                client.MaxResponseContentBufferSize = 64 * 1024;
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        }
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new()
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                // JsonWebTokenHandler de IAM conserva el alias URI de HMAC-SHA256; ambos nombres son el mismo algoritmo.
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256, SecurityAlgorithms.HmacSha256Signature], ValidateLifetime = true, RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                ValidateIssuer = !string.IsNullOrWhiteSpace(configuration["Jwt:Issuer"]), ValidIssuer = configuration["Jwt:Issuer"],
                ValidateAudience = !string.IsNullOrWhiteSpace(configuration["Jwt:Audience"]), ValidAudience = configuration["Jwt:Audience"],
                NameClaimType = ClaimTypes.NameIdentifier, RoleClaimType = ClaimTypes.Role
            };
            options.Events = new()
            {
                OnMessageReceived = context =>
                {
                    if (context.HttpContext.Request.Path.StartsWithSegments("/hubs/notifications") &&
                        context.Request.Query.TryGetValue("access_token", out var token)) context.Token = token;
                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    if (context.Principal?.Identity is ClaimsIdentity identity)
                        foreach (var role in identity.FindAll("role").ToArray())
                            identity.AddClaim(new(ClaimTypes.Role, role.Value));
                    if (context.Principal is null || !NotificationIdentity.Valid(context.Principal))
                    { context.Fail("NOTIFICATION_INVALID_IDENTITY"); return; }
                    var roles = context.Principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray();
                    if (roles.Length != 1) { context.Fail("NOTIFICATION_INVALID_IDENTITY"); return; }
                    if (!iam.AccountValidation.Enabled) return;
                    var token = context.SecurityToken switch
                    {
                        JsonWebToken jwt => jwt.EncodedToken,
                        JwtSecurityToken jwt => jwt.RawData,
                        _ => ""
                    };
                    if (string.IsNullOrWhiteSpace(token)) { context.Fail("NOTIFICATION_INVALID_IDENTITY"); return; }
                    var state = await context.HttpContext.RequestServices.GetRequiredService<INotificationIamAccountVerifier>()
                        .VerifyAsync(token, NotificationIdentity.UserId(context.Principal), NotificationIdentity.OrganizationId(context.Principal),
                            roles[0], context.HttpContext.RequestAborted);
                    if (state != NotificationIamAccountState.Active)
                    {
                        if (state == NotificationIamAccountState.Unavailable) context.HttpContext.Items["notification-iam-unavailable"] = true;
                        context.Fail("NOTIFICATION_IAM_ACCOUNT_REJECTED");
                    }
                },
                OnChallenge = async context =>
                {
                    if (!context.HttpContext.Items.ContainsKey("notification-iam-unavailable")) return;
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.Headers.CacheControl = "no-store";
                    await context.Response.WriteAsJsonAsync(new ProblemDetails
                    {
                        Status = 503, Title = "No se pudo verificar la cuenta.",
                        Extensions = { ["code"] = "NOTIFICATION_IAM_UNAVAILABLE" }
                    }, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: context.HttpContext.RequestAborted);
                }
            };
        });
        services.AddAuthorizationBuilder()
            .AddPolicy("notification-user", p => p.RequireAuthenticatedUser().RequireAssertion(c => NotificationIdentity.Valid(c.User)))
            .AddPolicy("notification-admin", p => p.RequireAuthenticatedUser().RequireRole("ADMIN").RequireAssertion(c => NotificationIdentity.Valid(c.User)));
        return services;
    }
}
