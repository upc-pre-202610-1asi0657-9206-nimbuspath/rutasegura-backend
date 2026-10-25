using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace RutaSegura.TripTracking.Api.Security;

/// <summary>
/// Sección <c>Iam</c>: dónde está Iam y cómo se verifica que la cuenta del token siga vigente.
/// </summary>
public sealed class IamOptions
{
    public const string Section = "Iam";

    /// <summary>URL base del monolito (Iam). Ej.: <c>http://localhost:5000</c>, <c>http://monolith:8080</c>.</summary>
    public string? BaseUrl { get; set; }

    public IamAccountValidationOptions AccountValidation { get; set; } = new();
}

public sealed class IamAccountValidationOptions
{
    /// <summary>
    /// Verificar en Iam cada cuenta (con caché). Solo se apaga para desarrollar sin Iam (tokens de <c>/dev/tokens</c>).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Endpoint de Iam que devuelve la cuenta del token (aplica <c>IamPolicies.Account</c>).</summary>
    public string AccountPath { get; set; } = "/api/v1/users/me";

    /// <summary>Cuánto se recuerda la respuesta de Iam por token. Es el retraso máximo para que una cuenta revocada pierda acceso.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Cuánto se recuerda que Iam no respondió, para no golpearlo en cada request durante una caída.</summary>
    public TimeSpan UnavailableCacheDuration { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Qué hacer si Iam no responde. <c>Allow</c> (por defecto): basta el JWT, que ya se validó (firma, emisor,
    /// audiencia y vigencia de 8 h); así un SOS o el envío de posiciones no dependen de que el monolito esté arriba
    /// (BP-01, seguridad del alumno primero). <c>Deny</c>: 503 hasta que Iam vuelva.
    /// </summary>
    public IamUnavailableBehavior WhenUnavailable { get; set; } = IamUnavailableBehavior.Allow;
}

public enum IamUnavailableBehavior
{
    Allow,
    Deny
}

/// <summary>Identidad que trae el token (lo que se compara con la cuenta real en Iam).</summary>
public sealed record IamTokenIdentity(Guid UserId, string Role, Guid? OrganizationId);

public enum IamAccountState
{
    /// <summary>Iam reconoce la cuenta con el mismo rol y la misma organización, y la organización está activa.</summary>
    Active,

    /// <summary>Iam rechazó la cuenta (no existe, otro rol u organización, organización suspendida o token rechazado).</summary>
    Rejected,

    /// <summary>Iam no respondió o respondió con un error propio.</summary>
    Unavailable
}

public sealed record IamAccountCheck(IamAccountState State, string Reason)
{
    public static IamAccountCheck Active() => new(IamAccountState.Active, "ACTIVE");
    public static IamAccountCheck Rejected(string reason) => new(IamAccountState.Rejected, reason);
    public static IamAccountCheck Unavailable(string reason) => new(IamAccountState.Unavailable, reason);
}

/// <summary>Puerto: pregunta a Iam si la cuenta del token sigue vigente.</summary>
public interface IIamAccountVerifier
{
    Task<IamAccountCheck> VerifyAsync(string accessToken, IamTokenIdentity identity, CancellationToken cancellationToken);
}

/// <summary>
/// Adaptador HTTP hacia Iam (ACL): reenvía el mismo token a <c>GET /api/v1/users/me</c>. Iam aplica ahí su
/// <c>ActiveIamAccountHandler</c> (cuenta existente, mismo rol y organización, organización ACTIVE) y devuelve la
/// cuenta, que además se compara con los claims del token. La respuesta se guarda en memoria por hash del token.
/// </summary>
internal sealed class HttpIamAccountVerifier(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IOptions<IamOptions> options,
    ILogger<HttpIamAccountVerifier> logger) : IIamAccountVerifier
{
    public const string HttpClientName = "iam";

    public async Task<IamAccountCheck> VerifyAsync(string accessToken, IamTokenIdentity identity, CancellationToken cancellationToken)
    {
        var settings = options.Value.AccountValidation;
        var cacheKey = CacheKey(accessToken);
        if (cache.TryGetValue(cacheKey, out IamAccountCheck? cached) && cached is not null) return cached;

        var check = await AskIamAsync(accessToken, identity, settings, cancellationToken);
        var duration = check.State == IamAccountState.Unavailable ? settings.UnavailableCacheDuration : settings.CacheDuration;
        if (duration > TimeSpan.Zero) cache.Set(cacheKey, check, duration);
        return check;
    }

    private async Task<IamAccountCheck> AskIamAsync(string accessToken, IamTokenIdentity identity,
        IamAccountValidationOptions settings, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, settings.AccountPath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(settings.Timeout);
            using var response = await client.SendAsync(request, timeout.Token);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var account = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
                    return Matches(account, identity)
                        ? IamAccountCheck.Active()
                        : Reject(identity, "ACCOUNT_MISMATCH");
                case HttpStatusCode.Unauthorized:
                    return Reject(identity, "TOKEN_REJECTED_BY_IAM");
                case HttpStatusCode.Forbidden:
                    return Reject(identity, "ACCOUNT_NOT_ACTIVE");
                case HttpStatusCode.NotFound:
                    return Reject(identity, "ACCOUNT_NOT_FOUND");
                default:
                    logger.LogWarning("Iam respondió {Status} al verificar la cuenta {UserId}", (int)response.StatusCode, identity.UserId);
                    return IamAccountCheck.Unavailable($"IAM_STATUS_{(int)response.StatusCode}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Iam no respondió en {Timeout} al verificar la cuenta {UserId}", settings.Timeout, identity.UserId);
            return IamAccountCheck.Unavailable("IAM_TIMEOUT");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("Iam no está disponible ({Message}) al verificar la cuenta {UserId}", exception.Message, identity.UserId);
            return IamAccountCheck.Unavailable("IAM_UNREACHABLE");
        }
        catch (JsonException)
        {
            logger.LogWarning("Iam devolvió una cuenta ilegible para {UserId}", identity.UserId);
            return IamAccountCheck.Unavailable("IAM_INVALID_RESPONSE");
        }
    }

    private IamAccountCheck Reject(IamTokenIdentity identity, string reason)
    {
        logger.LogWarning("Iam rechazó la cuenta {UserId} ({Reason}): acceso denegado", identity.UserId, reason);
        return IamAccountCheck.Rejected(reason);
    }

    /// <summary>La cuenta de Iam (<c>UserResource</c>) debe ser la misma del token: id, rol y organización.</summary>
    internal static bool Matches(JsonElement account, IamTokenIdentity identity)
    {
        if (account.ValueKind != JsonValueKind.Object) return false;
        var id = Property(account, "id");
        var role = Property(account, "roleTier");
        var organization = Property(account, "organizationId");

        return id is { ValueKind: JsonValueKind.String } && id.Value.TryGetGuid(out var userId) && userId == identity.UserId &&
               role is { ValueKind: JsonValueKind.String } &&
               string.Equals(role.Value.GetString(), identity.Role, StringComparison.OrdinalIgnoreCase) &&
               OrganizationOf(organization) == identity.OrganizationId;
    }

    private static Guid? OrganizationOf(JsonElement? organization) =>
        organization is { ValueKind: JsonValueKind.String } value && value.TryGetGuid(out var id) && id != Guid.Empty ? id : null;

    private static JsonElement? Property(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        return null;
    }

    private static string CacheKey(string accessToken) =>
        "iam-account:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));
}

/// <summary>
/// El JWT no basta: la cuenta debe seguir vigente en Iam. Equivale al <c>ActiveIamAccountRequirement</c> del monolito,
/// pero preguntando a Iam por HTTP porque Trip &amp; Tracking es otro desplegable.
/// </summary>
/// <param name="RequiresOrganization">El token debe traer <c>organizationId</c> (endpoints de una empresa).</param>
public sealed record ActiveIamAccountRequirement(bool RequiresOrganization) : IAuthorizationRequirement;

internal sealed class ActiveIamAccountHandler(
    IIamAccountVerifier verifier,
    IOptions<IamOptions> options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ActiveIamAccountHandler> logger) : AuthorizationHandler<ActiveIamAccountRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveIamAccountRequirement requirement)
    {
        // Una política combinada ([Authorize] del controller + la de la acción) trae varios de estos requisitos:
        // se resuelven todos con una sola consulta a Iam.
        if (!context.PendingRequirements.Contains(requirement)) return;
        var pending = context.PendingRequirements.OfType<ActiveIamAccountRequirement>().ToList();

        var principal = context.User;
        if (principal.Identity?.IsAuthenticated != true) return;

        var userId = principal.UserId();
        var role = principal.IamRole();
        var organizationId = principal.OrganizationId();
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(role)) return;
        if (role is not (Roles.Admin or Roles.Driver or Roles.Parent)) return;
        if (organizationId is null && context.Requirements.OfType<ActiveIamAccountRequirement>().Any(r => r.RequiresOrganization)) return;
        // Si el rol del token ya no alcanza para la política, se responde 403 sin molestar a Iam.
        if (context.Requirements.OfType<RolesAuthorizationRequirement>().Any(r => !r.AllowedRoles.Any(principal.IsInRole))) return;

        var settings = options.Value.AccountValidation;
        if (!settings.Enabled)
        {
            SucceedAll(context, pending);
            return;
        }

        var http = context.Resource switch
        {
            HttpContext h => h,
            HubInvocationContext hub => hub.Context.GetHttpContext(),
            _ => httpContextAccessor.HttpContext
        };
        if (http is null) return;

        var token = await http.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token)) return;

        var check = await verifier.VerifyAsync(token, new IamTokenIdentity(userId, role, organizationId), http.RequestAborted);
        switch (check.State)
        {
            case IamAccountState.Active:
                SucceedAll(context, pending);
                break;
            case IamAccountState.Unavailable when settings.WhenUnavailable == IamUnavailableBehavior.Allow:
                logger.LogWarning("Iam no disponible ({Reason}): se acepta el JWT ya validado de {UserId}", check.Reason, userId);
                SucceedAll(context, pending);
                break;
            case IamAccountState.Unavailable:
                http.Items[SecurityProblems.FailureCodeKey] = SecurityErrorCodes.IamUnavailable;
                break;
            default:
                http.Items[SecurityProblems.FailureCodeKey] = SecurityErrorCodes.IamAccountRejected;
                break;
        }
    }

    private static void SucceedAll(AuthorizationHandlerContext context, IEnumerable<ActiveIamAccountRequirement> requirements)
    {
        foreach (var requirement in requirements) context.Succeed(requirement);
    }
}

internal static class IamAccountValidationExtensions
{
    public static IServiceCollection AddTripTrackingIamAccountValidation(this IServiceCollection services, IConfiguration configuration)
    {
        var iam = configuration.GetSection(IamOptions.Section).Get<IamOptions>() ?? new IamOptions();
        services.AddOptions<IamOptions>().Bind(configuration.GetSection(IamOptions.Section));

        Uri? baseAddress = null;
        if (iam.AccountValidation.Enabled &&
            (!Uri.TryCreate(iam.BaseUrl, UriKind.Absolute, out baseAddress) || baseAddress.Scheme is not ("http" or "https")))
            throw new InvalidOperationException(
                "Iam:BaseUrl debe ser la URL de Iam (monolito), p. ej. http://localhost:5000, para verificar las cuentas. " +
                "Solo para desarrollar sin Iam: Iam:AccountValidation:Enabled=false.");

        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddHttpClient(HttpIamAccountVerifier.HttpClientName, client =>
        {
            if (baseAddress is not null) client.BaseAddress = baseAddress;
            // El tiempo límite real lo pone IamAccountValidationOptions.Timeout por request.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton<IIamAccountVerifier, HttpIamAccountVerifier>();
        services.AddScoped<IAuthorizationHandler, ActiveIamAccountHandler>();
        return services;
    }
}
