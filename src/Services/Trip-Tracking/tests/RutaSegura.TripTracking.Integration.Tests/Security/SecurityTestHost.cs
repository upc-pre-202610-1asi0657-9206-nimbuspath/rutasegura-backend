using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.TripTracking.Api.Security;

namespace RutaSegura.TripTracking.Integration.Tests.Security;

/// <summary>
/// API real de Trip &amp; Tracking (Program.cs: autenticación, políticas, hub, ProblemDetails) SIN base de datos:
/// los casos de uso se reemplazan por <see cref="RecordingDispatcher"/> y el monolito por <see cref="StubIam"/>,
/// que responde <c>GET /api/v1/users/me</c> como lo hace Iam. Así se prueba solo la seguridad.
/// </summary>
public sealed class SecurityApiFactory(IDictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
{
    public const string Secret = "trip-tracking-security-tests-secret-0123456789";
    public const string Issuer = "rutasegura-iam";
    public const string Audience = "rutasegura-api";
    public const string IamBaseUrl = "http://iam.test";

    public StubIam Iam { get; } = new();
    public RecordingDispatcher Dispatcher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:TripTracking"] = "server=127.0.0.1;port=1;database=unused;user=unused;password=unused",
            ["ConnectionStrings:Redis"] = "",
            ["Jwt:Secret"] = Secret,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Iam:BaseUrl"] = IamBaseUrl,
            ["Iam:AccountValidation:Enabled"] = "true",
            ["Iam:AccountValidation:CacheDuration"] = "00:01:00",
            ["Iam:AccountValidation:UnavailableCacheDuration"] = "00:00:00",
            ["Iam:AccountValidation:WhenUnavailable"] = "Allow",
            ["Persistence:MigrateOnStartup"] = "false",
            ["RabbitMq:Enabled"] = "false",
            ["Scheduling:Enabled"] = "false",
            ["Seed:Demo"] = "false",
            ["DevTokens:Enabled"] = "false"
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>()) settings[key] = value;
        foreach (var (key, value) in settings) builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDispatcher>();
            services.AddSingleton<IDispatcher>(Dispatcher);
            services.AddHttpClient(HttpIamAccountVerifier.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Iam);
        });
    }

    public HttpClient Client(string? token = null)
    {
        var client = CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Token como el de Iam, con la cuenta registrada como vigente en el Iam simulado.</summary>
    public string ActiveAccount(string role, Guid? userId = null, Guid? organizationId = null)
    {
        var id = userId ?? Guid.NewGuid();
        Iam.Register(new StubIam.Account(id, role, organizationId));
        return IamTokens.Create(id, role, organizationId);
    }
}

/// <summary>Construye JWT igual que el <c>TokenService</c> de Iam, con variantes para simular tokens falsos.</summary>
public static class IamTokens
{
    public static string Create(Guid userId, string role, Guid? organizationId, string secret = SecurityApiFactory.Secret,
        string? issuer = SecurityApiFactory.Issuer, string? audience = SecurityApiFactory.Audience,
        DateTime? expires = null, DateTime? notBefore = null, bool withExpiration = true, bool signed = true,
        IEnumerable<Claim>? claims = null)
    {
        var subject = claims?.ToList() ?? new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, $"{userId:N}@rutasegura.test"),
            new(ClaimTypes.Name, "Usuario de prueba"),
            new(ClaimTypes.Role, role)
        };
        if (claims is null && organizationId is not null) subject.Add(new Claim("organizationId", organizationId.Value.ToString()));

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(subject),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = signed
                ? new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256Signature)
                : null
        };
        if (withExpiration)
        {
            descriptor.NotBefore = notBefore ?? now.AddMinutes(-1);
            descriptor.IssuedAt = notBefore ?? now.AddMinutes(-1);
            descriptor.Expires = expires ?? now.AddHours(8);
        }

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = withExpiration }.CreateToken(descriptor);
    }
}

/// <summary>
/// Iam simulado: responde <c>GET /api/v1/users/me</c> como el monolito (200 con <c>UserResource</c>, 401/403/404),
/// o se cae (<see cref="Down"/>) o responde 500 (<see cref="Failing"/>). Cuenta las llamadas recibidas.
/// </summary>
public sealed class StubIam : HttpMessageHandler
{
    public sealed record Account(Guid Id, string Role, Guid? OrganizationId, HttpStatusCode Status = HttpStatusCode.OK);

    private readonly ConcurrentDictionary<Guid, Account> _accounts = new();
    private int _calls;

    public bool Down { get; set; }
    public bool Failing { get; set; }
    public int Calls => Volatile.Read(ref _calls);
    public ConcurrentQueue<string> ReceivedTokens { get; } = new();
    public ConcurrentQueue<string> ReceivedPaths { get; } = new();

    public void Register(Account account) => _accounts[account.Id] = account;

    /// <summary>Iam responde ese estado para la cuenta (403: organización suspendida, 404: eliminada…).</summary>
    public void Respond(Guid userId, HttpStatusCode status) =>
        _accounts.AddOrUpdate(userId, id => new Account(id, "PARENT", null, status), (_, a) => a with { Status = status });

    /// <summary>Iam devuelve la cuenta con otros datos (rol u organización cambiados).</summary>
    public void Change(Guid userId, string role, Guid? organizationId) =>
        _accounts[userId] = new Account(userId, role, organizationId);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        ReceivedPaths.Enqueue(request.RequestUri!.AbsolutePath);
        if (Down) throw new HttpRequestException("Connection refused (iam.test)");
        if (Failing) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var token = request.Headers.Authorization is { Scheme: "Bearer", Parameter: { } value } ? value : null;
        if (token is null || request.RequestUri.AbsolutePath != "/api/v1/users/me")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        ReceivedTokens.Enqueue(token);

        var userId = Guid.TryParse(new JsonWebToken(token).Claims
            .FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
        if (!_accounts.TryGetValue(userId, out var account))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        if (account.Status != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(account.Status));

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                id = account.Id,
                firstName = "Usuario",
                lastName = "Prueba",
                email = $"{account.Id:N}@rutasegura.test",
                roleTier = account.Role,
                organizationId = account.OrganizationId
            }, options: new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
    }
}

/// <summary>Reemplaza a los casos de uso: registra qué llegó (con la identidad del token) y responde éxito vacío.</summary>
public sealed class RecordingDispatcher : IDispatcher
{
    public ConcurrentQueue<object> Received { get; } = new();

    public T Last<T>() => Received.OfType<T>().Last();

    public Task<Result<TResponse>> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        Received.Enqueue(command);
        return Task.FromResult(Result<TResponse>.Success(default!));
    }

    public Task<Result<TResponse>> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        Received.Enqueue(query);
        return Task.FromResult(Result<TResponse>.Success(default!));
    }
}

internal static class SecurityAssertions
{
    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
