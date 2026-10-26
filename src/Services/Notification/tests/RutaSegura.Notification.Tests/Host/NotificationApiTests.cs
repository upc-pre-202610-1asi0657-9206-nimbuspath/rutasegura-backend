using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace RutaSegura.Notification.Tests.Host;

public sealed class NotificationApiTests
{
    [Fact]
    public async Task SignedAdminTokenIsInsufficientWhenCurrentOperatorMembershipWasRevoked()
    {
        await using var factory = new NotificationApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(role: "ADMIN"));
        factory.Directory.Operators.Clear();
        var response = await client.PostAsJsonAsync("/api/v1/announcements", new { requestId = Guid.NewGuid(), title = "Aviso", body = "Contenido" }, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        factory.Store.Announcements.ShouldBeEmpty();
    }

    [Fact]
    public async Task IamStyleUriClaimsAreAcceptedWithoutDependingOnIamAssembly()
    {
        await using var factory = new NotificationApiFactory();
        using var client = factory.CreateClient();
        var descriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity([
                new(System.Security.Claims.ClaimTypes.NameIdentifier, factory.Guardian.ToString()),
                new(System.Security.Claims.ClaimTypes.Role, "PARENT"), new("organizationId", factory.Organization.ToString())]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new(new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(NotificationApiFactory.Secret)), Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
        };
        client.DefaultRequestHeaders.Authorization = new("Bearer", new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().CreateToken(descriptor));
        (await client.GetAsync("/api/v1/notifications", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("https://web.test", true)]
    [InlineData("https://foreign.test", false)]
    public async Task HubNegotiationRequiresAnAllowedBrowserOrigin(string origin, bool allowed)
    {
        await using var factory = new NotificationApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token());
        client.DefaultRequestHeaders.Add("Origin", origin);
        (await client.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("foreign-signature")]
    [InlineData("no-tenant")]
    [InlineData("unknown-role")]
    [InlineData("empty-user")]
    public async Task InvalidIdentityNeverReachesUseCases(string scenario)
    {
        await using var factory = new NotificationApiFactory();
        using var client = factory.CreateClient();
        var token = scenario switch
        {
            "expired" => factory.Token(expired: true), "foreign-signature" => factory.Token(secret: new string('x', 40)),
            "no-tenant" => factory.Token(includeTenant: false), "unknown-role" => factory.Token(role: "ROOT"),
            "empty-user" => factory.Token(user: Guid.Empty), _ => null
        };
        if (token is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        (await client.GetAsync("/api/v1/notifications", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        factory.UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task QueryTokensOnlyWorkForSignalR_NotRest()
    {
        await using var factory = new NotificationApiFactory();
        using var client = factory.CreateClient();
        (await client.GetAsync("/api/v1/notifications?access_token=" + factory.Token(), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InboxDetailAndReadRespectOwnershipTenantAndCurrentStudentLink()
    {
        await using var factory = new NotificationApiFactory();
        var notice = factory.SeedNotice();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token());
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications", TestContext.Current.CancellationToken)).GetArrayLength().ShouldBe(1);
        (await client.PatchAsync($"/api/v1/notifications/{notice.Id}/read", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        notice.ReadAt.ShouldNotBeNull();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(organization: Guid.NewGuid()));
        (await client.GetAsync($"/api/v1/notifications/{notice.Id}", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token());
        factory.Directory.Students.Clear();
        (await client.GetAsync($"/api/v1/notifications/{notice.Id}", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DirectoryOutageReturns503InsteadOfExposingUnverifiedData()
    {
        await using var factory = new NotificationApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token());
        factory.Directory.Unavailable = true;
        var response = await client.GetAsync("/api/v1/notifications", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("Directorio no disponible");
    }

    [Fact]
    public async Task OnlyAdministratorCanPublishAndRequestIdIsIdempotent()
    {
        await using var factory = new NotificationApiFactory();
        using var client = factory.CreateClient();
        var body = new { requestId = Guid.NewGuid(), title = "Aviso", body = "Cambio de horario" };
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token());
        (await client.PostAsJsonAsync("/api/v1/announcements", body, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        factory.Directory.AnnouncementRecipients.Add(new(factory.Organization, factory.Guardian));
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(role: "ADMIN"));
        (await client.PostAsJsonAsync("/api/v1/announcements", body, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/v1/announcements", body, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Store.Notifications.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task PushRegistrationDoesNotEchoTokenAndForeignUserCannotDisableIt()
    {
        await using var factory = new NotificationApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token());
        var response = await client.PutAsJsonAsync($"/api/v1/push-subscriptions/{Guid.NewGuid()}", new { token = "very-private-token", platform = "Web" }, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain("very-private-token");
        var subscriptions = await client.GetFromJsonAsync<JsonElement>("/api/v1/push-subscriptions", TestContext.Current.CancellationToken);
        subscriptions.GetArrayLength().ShouldBe(1);
        subscriptions.ToString().ShouldNotContain("very-private-token");
        var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(user: Guid.NewGuid()));
        (await client.DeleteAsync($"/api/v1/push-subscriptions/{id}", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token());
        (await client.DeleteAsync($"/api/v1/push-subscriptions/{id}", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/push-subscriptions", TestContext.Current.CancellationToken)).GetArrayLength().ShouldBe(0);
    }
}
