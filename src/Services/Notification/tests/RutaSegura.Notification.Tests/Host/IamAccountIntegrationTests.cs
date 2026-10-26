extern alias notificationApi;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using IamVerifier = notificationApi::RutaSegura.Notification.Api.Security.NotificationIamAccountVerifier;
using IamOptions = notificationApi::RutaSegura.Notification.Api.Security.NotificationIamOptions;

namespace RutaSegura.Notification.Tests.Host;

public sealed class IamAccountIntegrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Issuer = "rutasegura-iam", Audience = "rutasegura-api";

    [Fact]
    public async Task OriginalIamJwtIsDelegatedAndRevocationBlocksTheNextRequest_WithNoPositiveCache()
    {
        using var iam = new IamHandler();
        await using var factory = Factory(iam);
        iam.Account = new { id = factory.Guardian, organizationId = factory.Organization, roleTier = "PARENT" };
        using var client = factory.CreateClient();
        var token = Token(factory);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        (await client.GetAsync("/api/v1/notifications", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        iam.LastToken.ShouldBe(token);
        iam.LastPath.ShouldBe("/api/v1/users/me");
        iam.Status = HttpStatusCode.Forbidden;
        (await client.GetAsync("/api/v1/notifications", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        iam.Calls.ShouldBe(2);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("organization")]
    [InlineData("role")]
    [InlineData("missing-organization")]
    public async Task AnIamResponseMustMatchTheSignedUserOrganizationAndRole(string mismatch)
    {
        using var iam = new IamHandler();
        await using var factory = Factory(iam);
        iam.Account = new
        {
            id = mismatch == "user" ? Guid.NewGuid() : factory.Guardian,
            organizationId = mismatch == "missing-organization" ? (Guid?)null : mismatch == "organization" ? Guid.NewGuid() : factory.Organization,
            roleTier = mismatch == "role" ? "ADMIN" : "PARENT"
        };
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(factory));
        (await client.GetAsync("/api/v1/notifications", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("invalid-json")]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("oversized")]
    public async Task IamFailureDeniesAccessAndReturnsSanitized503(string failure)
    {
        using var iam = new IamHandler { Failure = failure };
        await using var factory = Factory(iam);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(factory));
        using var response = await client.GetAsync("/api/v1/notifications", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldContain("NOTIFICATION_IAM_UNAVAILABLE");
        body.ShouldNotContain("sensitive");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Theory]
    [InlineData("foreign-issuer")]
    [InlineData("foreign-audience")]
    [InlineData("multiple-roles")]
    [InlineData("no-tenant")]
    public async Task InvalidTokensAreRejectedBeforeContactingIam(string invalid)
    {
        using var iam = new IamHandler();
        await using var factory = Factory(iam);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(factory, invalid));
        (await client.GetAsync("/api/v1/notifications", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        iam.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task SignalRNegotiationChecksTheLiveIamAccount()
    {
        using var iam = new IamHandler { Status = HttpStatusCode.Forbidden };
        await using var factory = Factory(iam);
        using var client = factory.CreateClient();
        var token = Token(factory);
        (await client.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1&access_token=" + token, null, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        iam.LastToken.ShouldBe(token);
    }

    [Theory]
    [InlineData("http://iam.internal", false)]
    [InlineData("https://user:secret@iam.internal", false)]
    [InlineData("https://iam.internal?token=secret", false)]
    public void UnsafeIamEndpointConfigurationIsRejected(string url, bool allowHttp)
    {
        Should.Throw<InvalidOperationException>(() => new IamOptions { BaseUrl = url, AllowInsecureHttp = allowHttp }.Validate());
    }

    private static NotificationApiFactory Factory(IamHandler handler) => new(new()
    {
        ["Iam:AccountValidation:Enabled"] = "true", ["Iam:BaseUrl"] = "https://iam.internal",
        ["Iam:AccountValidation:Timeout"] = "00:00:00.200", ["Jwt:Issuer"] = Issuer, ["Jwt:Audience"] = Audience
    }, services => services.AddHttpClient(IamVerifier.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler));

    private static string Token(NotificationApiFactory factory, string? invalid = null)
    {
        // Claim URI y roleTier coinciden con el contrato actual de IAM; no se referencia su ensamblado.
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, factory.Guardian.ToString()), new(ClaimTypes.Role, "PARENT") };
        if (invalid != "no-tenant") claims.Add(new("organizationId", factory.Organization.ToString()));
        if (invalid == "multiple-roles") claims.Add(new(ClaimTypes.Role, "ADMIN"));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: invalid == "foreign-issuer" ? "foreign" : Issuer, audience: invalid == "foreign-audience" ? "foreign" : Audience,
            claims: claims, expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(NotificationApiFactory.Secret)), SecurityAlgorithms.HmacSha256)));
    }

    private sealed class IamHandler : HttpMessageHandler
    {
        public object Account { get; set; } = new { };
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string? Failure { get; init; }
        public string? LastToken { get; private set; }
        public string? LastPath { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastToken = request.Headers.Authorization?.Parameter;
            LastPath = request.RequestUri!.AbsolutePath;
            if (Failure == "network") throw new HttpRequestException("sensitive upstream details");
            if (Failure == "timeout") await Task.Delay(Timeout.Infinite, ct);
            return Failure switch
            {
                "error" => new(HttpStatusCode.InternalServerError) { Content = new StringContent("sensitive") },
                "invalid-json" => new(HttpStatusCode.OK) { Content = new StringContent("sensitive invalid JSON") },
                "oversized" => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 70 * 1024)) },
                _ => new(Status) { Content = JsonContent.Create(Account) }
            };
        }
    }
}
