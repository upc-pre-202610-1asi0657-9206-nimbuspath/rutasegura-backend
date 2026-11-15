using System.Net;
using Microsoft.EntityFrameworkCore;
using Monolith.Iam.Domain.Model.Aggregates;
using Monolith.Shared.Domain.Model.ValueObjects;
using RutaSegura.Monolith.Administration.IntegrationTests.Support;

namespace RutaSegura.Monolith.Administration.IntegrationTests;

/// <summary>
///     Security Iam provides to Administration: authentication with Iam's JWT, role policies
///     (ADMIN / DRIVER), tenant isolation by the <c>organizationId</c> of the token, and a check on every request
///     that the account behind the token is still valid in Iam.
/// </summary>
public sealed class IamSecurityTests : IDisposable
{
    private static readonly string[] AdministratorEndpoints =
    [
        "/api/v1/roster/drivers", "/api/v1/roster/vehicles", "/api/v1/routes", "/api/v1/routes/quota",
        "/api/v1/operations/alerts"
    ];

    private readonly MonolithApp _app = new();

    public void Dispose() => _app.Dispose();

    // ── Authentication (401) ─────────────────────────────────────────────────

    [Fact]
    public async Task WithoutToken_EveryAdministrationEndpointAnswers401ProblemDetails()
    {
        var anonymous = _app.CreateClient();

        foreach (var endpoint in AdministratorEndpoints.Append("/api/v1/roster/drivers/me"))
        {
            using var response = await anonymous.Get(endpoint);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, endpoint);
            response.Headers.WwwAuthenticate.ToString().ShouldContain("Bearer");
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        }
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_Answers401()
    {
        var forged = TestTokens.Create("this-is-not-the-secret-of-iam-0123456789-abcdefgh", SeedAccounts.Admin,
            "ADMIN", SeedAccounts.Organization);

        await _app.WithToken(forged).Get("/api/v1/routes").ExpectAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredToken_Answers401()
    {
        var expired = _app.Token(SeedAccounts.Admin, "ADMIN", SeedAccounts.Organization,
            notBefore: DateTime.UtcNow.AddHours(-3), expires: DateTime.UtcNow.AddHours(-1));

        await _app.WithToken(expired).Get("/api/v1/routes").ExpectAsync(HttpStatusCode.Unauthorized);
    }

    // ── Roles (403) ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ParentAccount_IsForbiddenFromAdministration()
    {
        var parent = await _app.SignInAsync(SeedAccounts.ParentEmail, SeedAccounts.ParentPassword);

        foreach (var endpoint in AdministratorEndpoints)
            await parent.Get(endpoint).ExpectAsync(HttpStatusCode.Forbidden);
        await parent.Get("/api/v1/roster/drivers/me").ExpectAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DriverAccount_CannotManageTheOperation_ButReadsItsOwnProfileAndRoutes()
    {
        var driver = await _app.SignInAsync(SeedAccounts.DriverEmail, SeedAccounts.DriverPassword);

        await driver.Get("/api/v1/routes").ExpectAsync(HttpStatusCode.Forbidden);
        await driver.PostJson("/api/v1/roster/vehicles",
            new { licensePlate = "ABC-123", seatingCapacity = 15, brand = "Toyota", model = "Hiace" })
            .ExpectAsync(HttpStatusCode.Forbidden);
        (await driver.Get("/api/v1/roster/drivers/me").ExpectAsync(HttpStatusCode.NotFound))
            .Code().ShouldBe("DriverNotFound"); // the account has no roster profile yet

        var admin = await _app.AdminAsync();
        var driverId = await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver);
        var routeId = await admin.ReadyRouteAsync(driverId, await admin.RegisterVehicleAsync());

        var profile = await driver.Get("/api/v1/roster/drivers/me").ExpectAsync(HttpStatusCode.OK);
        profile.Id().ShouldBe(driverId);
        profile.GetProperty("userId").GetGuid().ShouldBe(SeedAccounts.Driver);
        var routes = await driver.Get("/api/v1/roster/drivers/me/routes").ExpectAsync(HttpStatusCode.OK);
        routes.GetArrayLength().ShouldBe(1);
        routes[0].Id().ShouldBe(routeId);
    }

    [Fact]
    public async Task AnotherDriver_DoesNotSeeRoutesOfOtherDrivers()
    {
        var admin = await _app.AdminAsync();
        var driverId = await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver);
        await admin.ReadyRouteAsync(driverId, await admin.RegisterVehicleAsync());
        await admin.RegisterVerifiedDriverAsync(SeedAccounts.SecondDriver, document: "47654321");
        var luis = await _app.SignInAsync("luis.torres@saferoute.pe", "driver123");

        (await luis.Get("/api/v1/roster/drivers/me/routes").ExpectAsync(HttpStatusCode.OK)).GetArrayLength()
            .ShouldBe(0);
    }

    // ── Tenant isolation (organizationId comes from the token) ───────────────

    [Fact]
    public async Task AdministratorOfAnotherOrganization_CannotSeeOrTouchThisOrganizationsData()
    {
        var admin = await _app.AdminAsync();
        var routeId = await admin.CreateRouteAsync();
        var vehicleId = await admin.RegisterVehicleAsync();
        var otherAdmin = await OtherOrganizationAdminAsync();

        (await otherAdmin.Get("/api/v1/routes").ExpectAsync(HttpStatusCode.OK)).GetArrayLength().ShouldBe(0);
        (await otherAdmin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.NotFound))
            .Code().ShouldBe("RouteNotFound");
        (await otherAdmin.PostJson($"/api/v1/roster/vehicles/{vehicleId}/disable")
            .ExpectAsync(HttpStatusCode.NotFound)).Code().ShouldBe("VehicleNotFound");
        await otherAdmin.Delete($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.NotFound);

        // A driver account of another organization cannot be linked to this roster.
        (await otherAdmin.PostJson("/api/v1/roster/drivers", new
        {
            userId = SeedAccounts.Driver, firstName = "Carlos", lastName = "Ramirez", documentNumber = "45678912",
            licenseNumber = "Q45678912", licenseCategory = "A-IIb", licenseExpiresOn = "2030-12-31",
            phoneNumber = "987654321"
        }).ExpectAsync(HttpStatusCode.UnprocessableEntity)).Code().ShouldBe("DriverAccountNotValid");

        // The original administrator still sees its route untouched.
        (await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK))
            .GetProperty("status").GetString().ShouldBe("DRAFT");
    }

    // ── The account behind the token must still be valid in Iam ──────────────

    [Fact]
    public async Task TokenWhoseRoleNoLongerMatchesTheAccount_IsForbidden()
    {
        // Validly signed token that says ADMIN for an account that is PARENT in Iam (e.g. role changed after login).
        var stale = _app.Token(SeedAccounts.Parent, "ADMIN", SeedAccounts.Organization);

        await _app.WithToken(stale).Get("/api/v1/routes").ExpectAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TokenOfAnotherOrganizationThanTheAccount_IsForbidden()
    {
        var tampered = _app.Token(SeedAccounts.Admin, "ADMIN", Guid.NewGuid());

        await _app.WithToken(tampered).Get("/api/v1/routes").ExpectAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeletedAccount_LosesAccessEvenWithAnUnexpiredToken()
    {
        var admin = await _app.AdminAsync();
        await admin.Get("/api/v1/routes").ExpectAsync(HttpStatusCode.OK);

        await _app.WithDbAsync(async db =>
        {
            var user = await db.Set<User>().SingleAsync(u => u.Id == new UserId(SeedAccounts.Admin));
            db.Remove(user);
            return await db.SaveChangesAsync();
        });

        await admin.Get("/api/v1/routes").ExpectAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SuspendedOrganization_LosesAccessToAdministration()
    {
        var admin = await _app.AdminAsync();
        await admin.Get("/api/v1/routes").ExpectAsync(HttpStatusCode.OK);

        await _app.WithDbAsync(async db =>
        {
            var organization = await db.Set<Organization>()
                .SingleAsync(o => o.Id == new OrganizationId(SeedAccounts.Organization));
            organization.Suspend();
            return await db.SaveChangesAsync();
        });

        await admin.Get("/api/v1/routes").ExpectAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdministratorTokenWithoutOrganization_IsForbidden()
    {
        var withoutOrganization = _app.Token(SeedAccounts.Admin, "ADMIN", organizationId: null);

        await _app.WithToken(withoutOrganization).Get("/api/v1/routes").ExpectAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IamEndpoints_KeepWorkingWithTheCentralizedJwtConfiguration()
    {
        var admin = await _app.AdminAsync();

        (await admin.Get($"/api/v1/users/{SeedAccounts.Driver}").ExpectAsync(HttpStatusCode.OK))
            .GetProperty("roleTier").GetString().ShouldBe("DRIVER");
        await _app.CreateClient().Get("/api/v1/users").ExpectAsync(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> OtherOrganizationAdminAsync() =>
        (await _app.ProvisionOrganizationAsync("Movilidad Escolar Sur", "admin@movilidadsur.pe")).Administrator;
}
