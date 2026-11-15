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
    public const string ScopeClaim = "notification:organization";
    public static Guid OrganizationId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(ScopeClaim) ?? user.FindFirstValue("organizationId"), out var id) ? id : Guid.Empty;
    public static bool AccountValid(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true && UserId(user) != Guid.Empty &&
        (user.IsInRole("PARENT") || (OrganizationId(user) != Guid.Empty && (user.IsInRole("ADMIN") || user.IsInRole("DRIVER"))));
    public static string Group(Guid organization, Guid user) => $"notification:{organization:N}:{user:N}";
    public static bool Valid(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true && UserId(user) != Guid.Empty &&
        OrganizationId(user) != Guid.Empty && new[] { "PARENT", "ADMIN", "DRIVER" }.Any(user.IsInRole);
}

public sealed class HttpNotificationUserContext(IHttpContextAccessor accessor) : INotificationUserContext
{
    public NotificationUser? Current => accessor.HttpContext?.User is { } user && NotificationIdentity.Valid(user)
        ? new(NotificationIdentity.UserId(user), NotificationIdentity.OrganizationId(user), user.IsInRole("ADMIN")) : null;
}

public sealed class HttpNotificationAccountContext(IHttpContextAccessor accessor) : INotificationAccountContext
{
    public Guid UserId => accessor.HttpContext?.User is { } user ? NotificationIdentity.UserId(user) : Guid.Empty;
    public bool IsParent => accessor.HttpContext?.User.IsInRole("PARENT") == true;
    public Guid? OrganizationId => accessor.HttpContext?.User is { } user && NotificationIdentity.OrganizationId(user) is var id && id != Guid.Empty ? id : null;
}

public static class NotificationSecurity
{
    public static IServiceCollection AddNotificationSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"] ?? "";
        if (Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("Configura Jwt:Secret de al menos 32 bytes, compatible con IAM.");
        services.AddHttpContextAccessor();
        services.AddScoped<INotificationAccountContext, HttpNotificationAccountContext>();
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
            options.SaveToken = true;
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
                    {
                        // Never accept a client-supplied scope, even from an otherwise valid JWT.
                        foreach (var scope in identity.FindAll(NotificationIdentity.ScopeClaim).ToArray()) identity.RemoveClaim(scope);
                        foreach (var role in identity.FindAll("role").ToArray()) identity.AddClaim(new(ClaimTypes.Role, role.Value));
                    }
                    if (context.Principal is null || !NotificationIdentity.AccountValid(context.Principal))
                    { context.Fail("NOTIFICATION_INVALID_IDENTITY"); return; }
                    var roles = context.Principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray();
                    if (roles.Length != 1) { context.Fail("NOTIFICATION_INVALID_IDENTITY"); return; }
                    if (iam.AccountValidation.Enabled)
                    {
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
                        context.Fail("NOTIFICATION_IAM_ACCOUNT_REJECTED"); return;
                    }
                    }
                    if (string.Equals(context.HttpContext.Request.Path.Value?.TrimEnd('/'), "/api/v1/notifications/contexts", StringComparison.OrdinalIgnoreCase)) return;
                    var request = context.HttpContext.Request;
                    var requested = request.Headers["X-Organization-Id"].ToString();
                    if (request.Path.StartsWithSegments("/hubs/notifications") && string.IsNullOrWhiteSpace(requested)) requested = request.Query["organizationId"].ToString();
                    var claimedOrganization = NotificationIdentity.OrganizationId(context.Principal);
                    if (context.Principal.IsInRole("PARENT") && (claimedOrganization == Guid.Empty || !string.IsNullOrWhiteSpace(requested)))
                    {
                        try
                        {
                            var allowed = await context.HttpContext.RequestServices.GetRequiredService<INotificationRecipientDirectory>()
                                .CurrentOrganizationIdsAsync(NotificationIdentity.UserId(context.Principal), context.HttpContext.RequestAborted);
                            var selected = string.IsNullOrWhiteSpace(requested) && allowed.Count == 1 ? allowed.Single()
                                : Guid.TryParse(requested, out var org) ? org : Guid.Empty;
                            if (selected == Guid.Empty || !allowed.Contains(selected))
                            { context.HttpContext.Items["notification-scope-denied"] = true; context.Fail("NOTIFICATION_SCOPE_FORBIDDEN"); return; }
                            ((ClaimsIdentity)context.Principal.Identity!).AddClaim(new(NotificationIdentity.ScopeClaim, selected.ToString()));
                        }
                        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TimeoutException)
                        { context.HttpContext.Items["notification-directory-unavailable"] = true; context.Fail("NOTIFICATION_DIRECTORY_UNAVAILABLE"); }
                    }
                    else if (!string.IsNullOrWhiteSpace(requested) && (!Guid.TryParse(requested, out var org) || org != claimedOrganization))
                    { context.HttpContext.Items["notification-scope-denied"] = true; context.Fail("NOTIFICATION_SCOPE_FORBIDDEN"); }
                },
                OnChallenge = async context =>
                {
                    var unavailable = context.HttpContext.Items.ContainsKey("notification-iam-unavailable") || context.HttpContext.Items.ContainsKey("notification-directory-unavailable");
                    var denied = context.HttpContext.Items.ContainsKey("notification-scope-denied");
                    if (!unavailable && !denied) return;
                    context.HandleResponse();
                    context.Response.StatusCode = unavailable ? 503 : 403;
                    context.Response.Headers.CacheControl = "no-store";
                    await context.Response.WriteAsJsonAsync(new ProblemDetails
                    {
                        Status = context.Response.StatusCode, Title = unavailable ? "No se pudo verificar la cuenta o el vínculo." : "Selecciona una operación autorizada.",
                        Extensions = { ["code"] = denied ? "NOTIFICATION_SCOPE_FORBIDDEN" : context.HttpContext.Items.ContainsKey("notification-directory-unavailable") ? "NOTIFICATION_DIRECTORY_UNAVAILABLE" : "NOTIFICATION_IAM_UNAVAILABLE" }
                    }, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: context.HttpContext.RequestAborted);
                }
            };
        });
        services.AddAuthorizationBuilder()
            .AddPolicy("notification-account", p => p.RequireAuthenticatedUser().RequireAssertion(c => NotificationIdentity.AccountValid(c.User)))
            .AddPolicy("notification-user", p => p.RequireAuthenticatedUser().RequireAssertion(c => NotificationIdentity.Valid(c.User)))
            .AddPolicy("notification-admin", p => p.RequireAuthenticatedUser().RequireRole("ADMIN").RequireAssertion(c => NotificationIdentity.Valid(c.User)));
        return services;
    }
}
