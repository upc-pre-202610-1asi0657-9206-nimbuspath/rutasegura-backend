using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Monolith.Iam.Domain.Model.Aggregates;
using Monolith.Shared.Domain.Model.ValueObjects;
using RutaSegura.Monolith.Administration.IntegrationTests.Support;

namespace RutaSegura.Monolith.Administration.IntegrationTests;

/// <summary>
///     Iam account rules: parents self-register, each organization gets exactly one administrator when it is
///     provisioned (subscription), the administrator creates its drivers, and nobody reads or edits accounts or
///     organizations outside its own organization.
/// </summary>
public sealed class IamAccountSecurityTests : IDisposable
{
    private readonly MonolithApp _app = new();

    public void Dispose() => _app.Dispose();

    private static object Parent(string email, string? roleTier = null, Guid? organizationId = null,
        string password = "Password123") => new
    {
        firstName = "Jorge", lastName = "Salas", email, password, roleTier, organizationId
    };

    // ── Sign-up: only parents ────────────────────────────────────────────────

    [Fact]
    public async Task SignUp_Parent_CreatesAParentAccount()
    {
        var anonymous = _app.CreateClient();

        var parent = await anonymous.PostJson("/api/v1/users", Parent("nuevo.padre@gmail.com"))
            .ExpectAsync(HttpStatusCode.Created);
        var withRole = await anonymous.PostJson("/api/v1/users", Parent("otro.padre@gmail.com", "parent",
                SeedAccounts.Organization))
            .ExpectAsync(HttpStatusCode.Created);

        parent.GetProperty("roleTier").GetString().ShouldBe("PARENT");
        withRole.GetProperty("organizationId").GetGuid().ShouldBe(SeedAccounts.Organization);
        await _app.SignInAsync("nuevo.padre@gmail.com", "Password123");
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("DRIVER")]
    public async Task SignUp_AsAdministratorOrDriver_IsForbidden(string role)
    {
        var problem = await _app.CreateClient()
            .PostJson("/api/v1/users", Parent("intruso@gmail.com", role, SeedAccounts.Organization))
            .ExpectAsync(HttpStatusCode.Forbidden);

        problem.Code().ShouldBe("SelfRegistrationNotAllowed");
        (await _app.WithDbAsync(db => db.Set<User>().CountAsync())).ShouldBe(9); // only the seeded accounts
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("onlyletters")]
    [InlineData("1234567890")]
    public async Task SignUp_WeakPassword_Returns400(string password) =>
        (await _app.CreateClient().PostJson("/api/v1/users", Parent("debil@gmail.com", password: password))
            .ExpectAsync(HttpStatusCode.BadRequest)).Code().ShouldBe("WeakPassword");

    [Fact]
    public async Task SignUp_UnknownOrganization_Returns404() =>
        (await _app.CreateClient().PostJson("/api/v1/users", Parent("padre@gmail.com", organizationId: Guid.NewGuid()))
            .ExpectAsync(HttpStatusCode.NotFound)).Code().ShouldBe("OrganizationNotFound");

    // ── Provisioning: organization + its single administrator ────────────────

    [Fact]
    public async Task ProvisionOrganization_WithoutOrWithAWrongKey_IsRejected()
    {
        var body = new
        {
            name = "Empresa Pirata",
            administrator = new { firstName = "A", lastName = "B", email = "pirata@x.pe", password = "Password123" }
        };

        await _app.CreateClient().PostJson("/api/v1/organizations", body).ExpectAsync(HttpStatusCode.Unauthorized);
        var wrongKey = _app.CreateClient();
        wrongKey.DefaultRequestHeaders.Add("X-Provisioning-Key", "not-the-provisioning-key-0123456789-xx");
        await wrongKey.PostJson("/api/v1/organizations", body).ExpectAsync(HttpStatusCode.Forbidden);

        (await _app.WithDbAsync(db => db.Set<Organization>().CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task ProvisionOrganization_NotConfigured_EndpointIsDisabled()
    {
        using var app = new MonolithApp(settings: new Dictionary<string, string?> { ["Iam:Provisioning:ApiKey"] = "" });
        var platform = app.CreateClient();
        platform.DefaultRequestHeaders.Add("X-Provisioning-Key", MonolithApp.TestProvisioningKey);

        await platform.PostJson("/api/v1/organizations", new
        {
            name = "Empresa",
            administrator = new { firstName = "A", lastName = "B", email = "a@b.pe", password = "Password123" }
        }).ExpectAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProvisionOrganization_CreatesTheOrganizationWithItsOnlyAdministrator()
    {
        var (organizationId, administratorId, administrator) =
            await _app.ProvisionOrganizationAsync("Movilidad Escolar Sur", "admin@movilidadsur.pe");

        var me = await administrator.Get("/api/v1/users/me").ExpectAsync(HttpStatusCode.OK);
        me.Id().ShouldBe(administratorId);
        me.GetProperty("roleTier").GetString().ShouldBe("ADMIN");
        me.GetProperty("organizationId").GetGuid().ShouldBe(organizationId);
        var admins = await _app.WithDbAsync(db => db.Set<User>()
            .Where(u => u.OrganizationId == new OrganizationId(organizationId)).ToListAsync());
        admins.Count(u => u.IsAdmin()).ShouldBe(1);

        // The new administrator manages its own operation right away.
        await administrator.Get("/api/v1/routes").ExpectAsync(HttpStatusCode.OK);
    }

    // ── Drivers are created by the administrator of the organization ─────────

    [Fact]
    public async Task Administrator_CreatesADriverAccountOfItsOrganization_ThenRegistersItInTheRoster()
    {
        var admin = await _app.AdminAsync();

        var account = await admin.PostJson("/api/v1/users/drivers", new
        {
            firstName = "Pedro", lastName = "Huaman", email = "pedro.huaman@saferoute.pe", password = "Conductor2026"
        }).ExpectAsync(HttpStatusCode.Created);

        account.GetProperty("roleTier").GetString().ShouldBe("DRIVER");
        account.GetProperty("organizationId").GetGuid().ShouldBe(SeedAccounts.Organization);
        await admin.RegisterVerifiedDriverAsync(account.Id(), document: "48887777");
        var driver = await _app.SignInAsync("pedro.huaman@saferoute.pe", "Conductor2026");
        (await driver.Get("/api/v1/roster/drivers/me").ExpectAsync(HttpStatusCode.OK))
            .GetProperty("userId").GetGuid().ShouldBe(account.Id());
    }

    [Fact]
    public async Task OnlyAdministrators_CanCreateDriverAccounts()
    {
        var body = new { firstName = "X", lastName = "Y", email = "x.y@saferoute.pe", password = "Password123" };

        await _app.CreateClient().PostJson("/api/v1/users/drivers", body).ExpectAsync(HttpStatusCode.Unauthorized);
        await (await _app.SignInAsync(SeedAccounts.ParentEmail, SeedAccounts.ParentPassword))
            .PostJson("/api/v1/users/drivers", body).ExpectAsync(HttpStatusCode.Forbidden);
        await (await _app.SignInAsync(SeedAccounts.DriverEmail, SeedAccounts.DriverPassword))
            .PostJson("/api/v1/users/drivers", body).ExpectAsync(HttpStatusCode.Forbidden);
    }

    // ── Reading accounts ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListOfAccounts_IsOnlyForAdministratorsAndOnlyOfTheirOrganization()
    {
        var parent = await _app.SignInAsync(SeedAccounts.ParentEmail, SeedAccounts.ParentPassword);
        await parent.Get("/api/v1/users").ExpectAsync(HttpStatusCode.Forbidden);

        var (_, otherAdminId, otherAdmin) =
            await _app.ProvisionOrganizationAsync("Movilidad Escolar Sur", "admin@movilidadsur.pe");
        var otherAccounts = await otherAdmin.Get("/api/v1/users").ExpectAsync(HttpStatusCode.OK);
        otherAccounts.GetArrayLength().ShouldBe(1);
        otherAccounts[0].Id().ShouldBe(otherAdminId);

        var accounts = await (await _app.AdminAsync()).Get("/api/v1/users").ExpectAsync(HttpStatusCode.OK);
        accounts.GetArrayLength().ShouldBe(9);
        accounts.EnumerateArray().ShouldAllBe(a =>
            a.GetProperty("organizationId").GetGuid() == SeedAccounts.Organization);
    }

    [Fact]
    public async Task AccountById_IsVisibleToItselfAndToItsAdministratorOnly()
    {
        var parent = await _app.SignInAsync(SeedAccounts.ParentEmail, SeedAccounts.ParentPassword);
        var (_, _, otherAdmin) = await _app.ProvisionOrganizationAsync("Movilidad Escolar Sur", "admin@movilidadsur.pe");

        await parent.Get($"/api/v1/users/{SeedAccounts.Parent}").ExpectAsync(HttpStatusCode.OK);
        await parent.Get($"/api/v1/users/{SeedAccounts.Driver}").ExpectAsync(HttpStatusCode.NotFound);
        await otherAdmin.Get($"/api/v1/users/{SeedAccounts.Parent}").ExpectAsync(HttpStatusCode.NotFound);
        await (await _app.AdminAsync()).Get($"/api/v1/users/{SeedAccounts.Parent}").ExpectAsync(HttpStatusCode.OK);
        (await parent.Get("/api/v1/users/me").ExpectAsync(HttpStatusCode.OK)).Id().ShouldBe(SeedAccounts.Parent);
    }

    // ── Organizations ────────────────────────────────────────────────────────

    [Fact]
    public async Task Organization_IsReadByItsMembersAndEditedOnlyByItsAdministrator()
    {
        var parent = await _app.SignInAsync(SeedAccounts.ParentEmail, SeedAccounts.ParentPassword);
        var (otherId, _, otherAdmin) = await _app.ProvisionOrganizationAsync("Movilidad Escolar Sur",
            "admin@movilidadsur.pe");
        var path = $"/api/v1/organizations/{SeedAccounts.Organization}";

        await _app.CreateClient().Get(path).ExpectAsync(HttpStatusCode.Unauthorized);
        await parent.Get(path).ExpectAsync(HttpStatusCode.OK);
        await otherAdmin.Get(path).ExpectAsync(HttpStatusCode.NotFound);
        await parent.Get($"/api/v1/organizations/{otherId}").ExpectAsync(HttpStatusCode.NotFound);

        await parent.PutJson(path, new { name = "Hackeada" }).ExpectAsync(HttpStatusCode.Forbidden);
        await otherAdmin.PutJson(path, new { name = "Hackeada" }).ExpectAsync(HttpStatusCode.NotFound);
        (await (await _app.AdminAsync()).PutJson(path, new { name = "Transportes Lima Norte" })
            .ExpectAsync(HttpStatusCode.OK)).GetProperty("name").GetString().ShouldBe("Transportes Lima Norte");
    }

    // ── Sign-in ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SignIn_WrongPassword_Returns401() =>
        (await _app.CreateClient().PostJson("/api/v1/users/sign-in",
                new { email = SeedAccounts.AdminEmail, password = "incorrecta1" })
            .ExpectAsync(HttpStatusCode.Unauthorized)).Code().ShouldBe("InvalidCredentials");

    [Fact]
    public async Task SignIn_AccountOfASuspendedOrganization_IsRejected()
    {
        await _app.WithDbAsync(async db =>
        {
            var organization = await db.Set<Organization>()
                .SingleAsync(o => o.Id == new OrganizationId(SeedAccounts.Organization));
            organization.Suspend();
            return await db.SaveChangesAsync();
        });

        (await _app.CreateClient().PostJson("/api/v1/users/sign-in",
                new { email = SeedAccounts.DriverEmail, password = SeedAccounts.DriverPassword })
            .ExpectAsync(HttpStatusCode.Forbidden)).Code().ShouldBe("OrganizationNotActive");
    }

    // ── Tokens ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task IssuedToken_CarriesIssuerAudienceAndAShortLifetime()
    {
        var response = await _app.CreateClient().PostJson("/api/v1/users/sign-in",
            new { email = SeedAccounts.AdminEmail, password = SeedAccounts.AdminPassword });
        var body = await response.ReadJsonAsync();
        var token = new JsonWebToken(body.GetProperty("token").GetString());

        token.Issuer.ShouldBe("rutasegura-iam");
        token.Audiences.ShouldContain("rutasegura-api");
        (token.ValidTo - DateTime.UtcNow).ShouldBeLessThanOrEqualTo(TimeSpan.FromHours(8));
    }

    [Fact]
    public async Task TokenForAnotherAudience_Answers401()
    {
        var token = _app.Token(SeedAccounts.Admin, "ADMIN", SeedAccounts.Organization, audience: "other-api");

        await _app.WithToken(token).Get("/api/v1/routes").ExpectAsync(HttpStatusCode.Unauthorized);
    }
}
