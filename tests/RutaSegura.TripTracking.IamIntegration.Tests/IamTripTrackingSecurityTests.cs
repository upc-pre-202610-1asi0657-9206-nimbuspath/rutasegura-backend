extern alias monolith;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Application.Trips.Queries;
using RutaSegura.TripTracking.IamIntegration.Tests.Support;
using monolith::Monolith.Iam.Domain.Model.Aggregates;
using monolith::Monolith.Shared.Domain.Model.ValueObjects;

namespace RutaSegura.TripTracking.IamIntegration.Tests;

/// <summary>
/// De punta a punta: las cuentas y los tokens son los de Iam (monolito) y Trip &amp; Tracking aplica la seguridad
/// que Iam provee: acepta sus tokens, respeta sus roles y organizaciones y deja de aceptar una cuenta cuando Iam
/// la desactiva.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IamTripTrackingSecurityTests : IDisposable
{
    private const string DriverAgenda = "/api/v1/drivers/me/trips";
    private const string GuardianTrips = "/api/v1/guardians/me/trips";
    private const string OrganizationTrips = "/api/v1/trips";

    private readonly IamHost _iam = new();
    private readonly TripTrackingHost _tripTracking;

    public IamTripTrackingSecurityTests() => _tripTracking = new TripTrackingHost(_iam);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _tripTracking.Dispose();
        _iam.Dispose();
    }

    [Fact]
    public async Task DriverSignedInWithIam_OperatesItsTripsInTripTracking()
    {
        var token = await _iam.SignInAsync(SeedAccounts.DriverEmail, SeedAccounts.DriverPassword);
        var driver = _tripTracking.As(token);

        (await driver.GetAsync(DriverAgenda, Ct)).IsSuccessStatusCode.ShouldBeTrue();
        (await driver.PostAsJsonAsync($"/api/v1/trips/{Guid.NewGuid()}/sos", new { clientEventId = Guid.NewGuid() }, Ct))
            .IsSuccessStatusCode.ShouldBeTrue();

        _tripTracking.Dispatcher.Last<GetDriverAgendaQuery>().DriverId.ShouldBe(SeedAccounts.Driver);
        _tripTracking.Dispatcher.Last<RaiseSosCommand>().DriverId.ShouldBe(SeedAccounts.Driver);
        _tripTracking.IamLink.Calls.ShouldBe(1, "Iam confirma la cuenta una vez y la respuesta queda en caché");
    }

    [Fact]
    public async Task AdministratorFromIam_OnlyQueriesItsOwnOrganization()
    {
        var token = await _iam.SignInAsync(SeedAccounts.AdminEmail, SeedAccounts.AdminPassword);

        (await _tripTracking.As(token).GetAsync(OrganizationTrips, Ct)).IsSuccessStatusCode.ShouldBeTrue();

        _tripTracking.Dispatcher.Last<GetOrganizationTripsQuery>().OrganizationId.ShouldBe(SeedAccounts.Organization);
    }

    [Fact]
    public async Task ParentFromIam_FollowsItsChildrenButCannotActAsDriverOrAdministrator()
    {
        var token = await _iam.SignInAsync(SeedAccounts.ParentEmail, SeedAccounts.ParentPassword);
        var parent = _tripTracking.As(token);

        (await parent.GetAsync(GuardianTrips, Ct)).IsSuccessStatusCode.ShouldBeTrue();
        _tripTracking.Dispatcher.Last<GetGuardianTripsQuery>().GuardianId.ShouldBe(SeedAccounts.Parent);

        (await parent.GetAsync(DriverAgenda, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await parent.GetAsync(OrganizationTrips, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("driver@saferoute.pe", "driver123", "DRIVER")]
    [InlineData("admin@saferoute.pe", "admin123", "ADMIN")]
    [InlineData("parent@saferoute.pe", "parent123", "PARENT")]
    public async Task EveryIamRole_BecomesTheSameRequesterInTripTracking(string email, string password, string role)
    {
        var token = await _iam.SignInAsync(email, password);
        var me = await _iam.As(token).GetFromJsonAsync<JsonElement>("/api/v1/users/me", IamHost.Json, Ct);
        var tripId = Guid.NewGuid();

        (await _tripTracking.As(token).GetAsync($"/api/v1/trips/{tripId}/live", Ct)).IsSuccessStatusCode.ShouldBeTrue();

        var requester = _tripTracking.Dispatcher.Last<GetTripLiveStatusQuery>().Requester;
        requester.UserId.ShouldBe(me.GetProperty("id").GetGuid());
        requester.OrganizationId.ShouldBe(me.GetProperty("organizationId").GetGuid());
        requester.Role.ShouldBe(role switch
        {
            "DRIVER" => RequesterRole.Driver,
            "ADMIN" => RequesterRole.Admin,
            _ => RequesterRole.Guardian
        });
    }

    [Fact]
    public async Task ParentWhoSignsUpInIam_CanFollowTripsRightAway()
    {
        var signUp = await _iam.CreateClient().PostAsJsonAsync("/api/v1/users", new
        {
            firstName = "Rosa",
            lastName = "Quispe",
            email = "rosa.quispe@gmail.com",
            password = "Password123"
        }, IamHost.Json, Ct);
        signUp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var parentId = (await signUp.Content.ReadFromJsonAsync<JsonElement>(IamHost.Json, Ct)).GetProperty("id").GetGuid();

        var token = await _iam.SignInAsync("rosa.quispe@gmail.com", "Password123");

        (await _tripTracking.As(token).GetAsync(GuardianTrips, Ct)).IsSuccessStatusCode.ShouldBeTrue();
        _tripTracking.Dispatcher.Last<GetGuardianTripsQuery>().GuardianId.ShouldBe(parentId);
    }

    [Fact]
    public async Task DriverCreatedByTheAdministratorInIam_DrivesForThatOrganization()
    {
        var provision = _iam.CreateClient();
        provision.DefaultRequestHeaders.Add("X-Provisioning-Key", SharedSecurity.ProvisioningKey);
        var created = await provision.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = "Movilidad Escolar Surco",
            administrator = new { firstName = "Ana", lastName = "Paredes", email = "ana@surco.pe", password = "Password123" }
        }, IamHost.Json, Ct);
        created.EnsureSuccessStatusCode();
        var organization = (await created.Content.ReadFromJsonAsync<JsonElement>(IamHost.Json, Ct))
            .GetProperty("organization").GetProperty("id").GetGuid();

        var administrator = await _iam.SignInAsync("ana@surco.pe", "Password123");
        var driverAccount = await _iam.As(administrator).PostAsJsonAsync("/api/v1/users/drivers", new
        {
            firstName = "Raúl",
            lastName = "Huamán",
            email = "raul@surco.pe",
            password = "Password123"
        }, IamHost.Json, Ct);
        driverAccount.StatusCode.ShouldBe(HttpStatusCode.Created);
        var driverId = (await driverAccount.Content.ReadFromJsonAsync<JsonElement>(IamHost.Json, Ct)).GetProperty("id").GetGuid();

        var driverToken = await _iam.SignInAsync("raul@surco.pe", "Password123");
        var tripId = Guid.NewGuid();
        (await _tripTracking.As(driverToken).GetAsync($"/api/v1/trips/{tripId}/summary", Ct)).IsSuccessStatusCode.ShouldBeTrue();
        (await _tripTracking.As(administrator).GetAsync(OrganizationTrips, Ct)).IsSuccessStatusCode.ShouldBeTrue();

        _tripTracking.Dispatcher.Last<GetTripSummaryQuery>().Requester
            .ShouldBe(new Requester(driverId, RequesterRole.Driver, organization));
        _tripTracking.Dispatcher.Last<GetOrganizationTripsQuery>().OrganizationId.ShouldBe(organization);
    }

    [Fact]
    public async Task OrganizationSuspendedInIam_LosesAccessToTripTracking()
    {
        using var tripTracking = new TripTrackingHost(_iam, new Dictionary<string, string?>
        {
            ["Iam:AccountValidation:CacheDuration"] = "00:00:00" // sin caché: el efecto es inmediato
        });
        var token = await _iam.SignInAsync(SeedAccounts.DriverEmail, SeedAccounts.DriverPassword);
        var driver = tripTracking.As(token);
        (await driver.GetAsync(DriverAgenda, Ct)).IsSuccessStatusCode.ShouldBeTrue();

        await _iam.WithDbAsync(async db =>
        {
            var organization = await db.Set<Organization>().SingleAsync(o => o.Id == new OrganizationId(SeedAccounts.Organization), Ct);
            organization.Suspend();
            return await db.SaveChangesAsync(Ct);
        });

        var response = await driver.GetAsync(DriverAgenda, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
            .ShouldBe("IAM_ACCOUNT_REJECTED");
    }

    [Fact]
    public async Task TokenWithIamFormatButUnknownAccount_IsRejectedByIam()
    {
        // Alguien con el secreto (p. ej. filtrado) arma un token de un conductor que no existe en Iam.
        var forged = ForgedToken(Guid.NewGuid(), "DRIVER", SeedAccounts.Organization);

        var response = await _tripTracking.As(forged).GetAsync(DriverAgenda, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _tripTracking.Dispatcher.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task TokenWithAnotherRoleThanTheIamAccount_IsRejected()
    {
        // El padre existe en Iam, pero el token dice DRIVER.
        var forged = ForgedToken(SeedAccounts.Parent, "DRIVER", SeedAccounts.Organization);

        (await _tripTracking.As(forged).GetAsync(DriverAgenda, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MonolithDown_ATokenAlreadyIssuedByIamKeepsWorking()
    {
        var token = await _iam.SignInAsync(SeedAccounts.DriverEmail, SeedAccounts.DriverPassword);
        _tripTracking.IamLink.Down = true;

        var sos = await _tripTracking.As(token).PostAsJsonAsync($"/api/v1/trips/{Guid.NewGuid()}/sos",
            new { clientEventId = Guid.NewGuid() }, Ct);

        sos.IsSuccessStatusCode.ShouldBeTrue("Con WhenUnavailable=Allow basta el JWT ya validado (BP-01)");
        _tripTracking.IamLink.Calls.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task MonolithDown_WithDenyTheRequestWaitsForIam()
    {
        using var tripTracking = new TripTrackingHost(_iam, new Dictionary<string, string?>
        {
            ["Iam:AccountValidation:WhenUnavailable"] = "Deny"
        });
        var token = await _iam.SignInAsync(SeedAccounts.DriverEmail, SeedAccounts.DriverPassword);
        tripTracking.IamLink.Down = true;

        (await tripTracking.As(token).GetAsync(DriverAgenda, Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task TokenOfAnotherIssuer_IsRejectedByBothServices()
    {
        using var otherIam = new TripTrackingHost(_iam, new Dictionary<string, string?> { ["Jwt:Issuer"] = "otro-emisor" });
        var token = await _iam.SignInAsync(SeedAccounts.DriverEmail, SeedAccounts.DriverPassword);

        (await otherIam.As(token).GetAsync(DriverAgenda, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        otherIam.IamLink.Calls.ShouldBe(0);
    }

    private static string ForgedToken(Guid userId, string role, Guid organizationId)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
            new(System.Security.Claims.ClaimTypes.Role, role),
            new("organizationId", organizationId.ToString())
        };
        return new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().CreateToken(
            new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
            {
                Subject = new System.Security.Claims.ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1),
                Issuer = SharedSecurity.Issuer,
                Audience = SharedSecurity.Audience,
                SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                    new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(SharedSecurity.Secret)),
                    Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
            });
    }
}
