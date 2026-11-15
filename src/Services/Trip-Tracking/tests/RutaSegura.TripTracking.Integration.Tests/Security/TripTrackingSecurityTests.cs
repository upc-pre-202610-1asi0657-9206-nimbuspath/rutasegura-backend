using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Application.Trips.Queries;

namespace RutaSegura.TripTracking.Integration.Tests.Security;

/// <summary>
/// Seguridad que Iam provee a Trip &amp; Tracking: solo se aceptan JWT de Iam (secreto, emisor, audiencia,
/// algoritmo y vigencia), las políticas por rol y organización equivalen a las de Iam y, en cada request,
/// Iam confirma que la cuenta del token sigue vigente (con caché). No necesitan MySQL ni el monolito.
/// </summary>
[Trait("Category", "Security")]
public sealed class TripTrackingSecurityTests : IDisposable
{
    private const string DriverAgenda = "/api/v1/drivers/me/trips";
    private const string GuardianTrips = "/api/v1/guardians/me/trips";
    private const string OrganizationTrips = "/api/v1/trips";

    private static readonly Guid Organization = Guid.NewGuid();
    private readonly SecurityApiFactory _api = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _api.Dispose();

    // ───────────────────────── Autenticación: solo tokens de Iam ─────────────────────────

    [Fact]
    public async Task WithoutToken_Returns401ProblemWithBearerChallenge()
    {
        var response = await _api.Client().GetAsync(DriverAgenda, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldBe("Bearer");
        (await response.ProblemCodeAsync()).ShouldBe("UNAUTHORIZED");
        _api.Iam.Calls.ShouldBe(0);
        _api.Dispatcher.Received.ShouldBeEmpty();
    }

    public static TheoryData<string, string> ForgedTokens()
    {
        var user = Guid.NewGuid();
        return new TheoryData<string, string>
        {
            { "otro secreto", IamTokens.Create(user, "DRIVER", Organization, secret: "another-secret-that-is-not-iam-0123456789") },
            { "otro emisor", IamTokens.Create(user, "DRIVER", Organization, issuer: "someone-else") },
            { "otra audiencia", IamTokens.Create(user, "DRIVER", Organization, audience: "another-api") },
            { "sin emisor ni audiencia (formato anterior)", IamTokens.Create(user, "DRIVER", Organization, issuer: null, audience: null) },
            { "sin firma (alg none)", IamTokens.Create(user, "DRIVER", Organization, signed: false) },
            { "sin vencimiento", IamTokens.Create(user, "DRIVER", Organization, withExpiration: false) },
            { "todavía no vigente", IamTokens.Create(user, "DRIVER", Organization, notBefore: DateTime.UtcNow.AddMinutes(10), expires: DateTime.UtcNow.AddHours(2)) },
            { "texto que no es JWT", "not-a-jwt" }
        };
    }

    [Theory]
    [MemberData(nameof(ForgedTokens))]
    public async Task TokenNotIssuedByIam_Returns401(string scenario, string token)
    {
        var response = await _api.Client(token).GetAsync(DriverAgenda, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, scenario);
        (await response.ProblemCodeAsync()).ShouldBe("INVALID_TOKEN", scenario);
        _api.Iam.Calls.ShouldBe(0, "Un token inválido no llega a consultarse en Iam");
        _api.Dispatcher.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExpiredIamToken_Returns401TokenExpired()
    {
        var token = IamTokens.Create(Guid.NewGuid(), "DRIVER", Organization,
            notBefore: DateTime.UtcNow.AddHours(-9), expires: DateTime.UtcNow.AddSeconds(-1));

        var response = await _api.Client(token).GetAsync(DriverAgenda, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ProblemCodeAsync()).ShouldBe("TOKEN_EXPIRED");
    }

    [Fact]
    public async Task ClaimsInAnotherFormat_AreNotTakenAsIamRoles()
    {
        // Firmado con el secreto correcto, pero con claims "sub"/"role" (formato que antes se aceptaba como respaldo).
        var user = Guid.NewGuid();
        var token = IamTokens.Create(user, "DRIVER", Organization, claims:
        [
            new Claim("sub", user.ToString()),
            new Claim("role", "DRIVER"),
            new Claim("organizationId", Organization.ToString())
        ]);

        var response = await _api.Client(token).GetAsync(DriverAgenda, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _api.Dispatcher.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnonymousEndpoints_StayAnonymous()
    {
        (await _api.Client().GetAsync("/health/live", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        _api.Iam.Calls.ShouldBe(0);
    }

    // ───────────────────────── Autorización: roles y organización ─────────────────────────

    [Theory]
    [InlineData("PARENT", DriverAgenda)]
    [InlineData("ADMIN", DriverAgenda)]
    [InlineData("DRIVER", GuardianTrips)]
    [InlineData("ADMIN", GuardianTrips)]
    [InlineData("DRIVER", OrganizationTrips)]
    [InlineData("PARENT", OrganizationTrips)]
    public async Task RoleWithoutPermission_Returns403(string role, string endpoint)
    {
        var token = _api.ActiveAccount(role, organizationId: Organization);

        var response = await _api.Client(token).GetAsync(endpoint, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ProblemCodeAsync()).ShouldBe("FORBIDDEN");
        _api.Dispatcher.Received.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("DRIVER", DriverAgenda)]
    [InlineData("ADMIN", OrganizationTrips)]
    public async Task DriverOrAdministratorWithoutOrganization_Returns403WithoutAskingIam(string role, string endpoint)
    {
        var token = _api.ActiveAccount(role, organizationId: null);

        (await _api.Client(token).GetAsync(endpoint, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _api.Iam.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task UnknownRole_IsForbiddenEvenOnEndpointsForAnyAccount()
    {
        var tripId = Guid.NewGuid();
        var token = _api.ActiveAccount("SUPERUSER", organizationId: Organization);

        (await _api.Client(token).GetAsync($"/api/v1/trips/{tripId}/live", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ───────────────────────── La identidad sale del token de Iam ─────────────────────────

    [Fact]
    public async Task Driver_ReachesItsAgendaWithTheIamUserId()
    {
        var driver = Guid.NewGuid();
        var token = _api.ActiveAccount("DRIVER", driver, Organization);

        var response = await _api.Client(token).GetAsync(DriverAgenda, Ct);

        response.IsSuccessStatusCode.ShouldBeTrue();
        _api.Dispatcher.Last<GetDriverAgendaQuery>().DriverId.ShouldBe(driver);
        _api.Iam.ReceivedTokens.ToArray().ShouldBe([token]); // reenvía a Iam el mismo token
        _api.Iam.ReceivedPaths.ShouldAllBe(path => path == "/api/v1/users/me");
    }

    [Fact]
    public async Task Administrator_OnlyQueriesTheOrganizationOfItsToken()
    {
        var token = _api.ActiveAccount("ADMIN", organizationId: Organization);

        (await _api.Client(token).GetAsync(OrganizationTrips, Ct)).IsSuccessStatusCode.ShouldBeTrue();

        _api.Dispatcher.Last<GetOrganizationTripsQuery>().OrganizationId.ShouldBe(Organization);
    }

    [Theory]
    [InlineData("DRIVER", RequesterRole.Driver)]
    [InlineData("ADMIN", RequesterRole.Admin)]
    [InlineData("PARENT", RequesterRole.Guardian)]
    public async Task TripViewer_GetsTheRequesterOfItsIamRole(string role, RequesterRole expected)
    {
        var user = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var token = _api.ActiveAccount(role, user, Organization);

        (await _api.Client(token).GetAsync($"/api/v1/trips/{tripId}/live", Ct)).IsSuccessStatusCode.ShouldBeTrue();

        var query = _api.Dispatcher.Last<GetTripLiveStatusQuery>();
        query.Requester.ShouldBe(new Requester(user, expected, Organization));
    }

    [Fact]
    public async Task Parent_ReachesTheTripsOfItsChildren()
    {
        var parent = Guid.NewGuid();
        var token = _api.ActiveAccount("PARENT", parent, organizationId: null);

        (await _api.Client(token).GetAsync(GuardianTrips, Ct)).IsSuccessStatusCode.ShouldBeTrue();

        _api.Dispatcher.Last<GetGuardianTripsQuery>().GuardianId.ShouldBe(parent);
    }

    [Fact]
    public async Task DriverSos_UsesTheDriverOfTheToken()
    {
        var driver = Guid.NewGuid();
        var token = _api.ActiveAccount("DRIVER", driver, Organization);

        var response = await _api.Client(token).PostAsJsonAsync($"/api/v1/trips/{Guid.NewGuid()}/sos",
            new { clientEventId = Guid.NewGuid() }, Ct);

        response.IsSuccessStatusCode.ShouldBeTrue();
        _api.Dispatcher.Last<RaiseSosCommand>().DriverId.ShouldBe(driver);
    }

    // ───────────────────────── La cuenta debe seguir vigente en Iam ─────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "cuenta eliminada")]
    [InlineData(HttpStatusCode.Forbidden, "organización suspendida o cuenta que ya no coincide")]
    [InlineData(HttpStatusCode.Unauthorized, "Iam ya no acepta el token")]
    public async Task AccountNoLongerValidInIam_Returns403(HttpStatusCode iamAnswer, string scenario)
    {
        var driver = Guid.NewGuid();
        var token = _api.ActiveAccount("DRIVER", driver, Organization);
        _api.Iam.Respond(driver, iamAnswer);

        var response = await _api.Client(token).GetAsync(DriverAgenda, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, scenario);
        (await response.ProblemCodeAsync()).ShouldBe("IAM_ACCOUNT_REJECTED", scenario);
        _api.Dispatcher.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task RoleChangedInIam_Returns403()
    {
        var user = Guid.NewGuid();
        var token = _api.ActiveAccount("DRIVER", user, Organization);
        _api.Iam.Change(user, "PARENT", Organization);

        var response = await _api.Client(token).GetAsync(DriverAgenda, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ProblemCodeAsync()).ShouldBe("IAM_ACCOUNT_REJECTED");
    }

    [Fact]
    public async Task OrganizationChangedInIam_Returns403()
    {
        var user = Guid.NewGuid();
        var token = _api.ActiveAccount("ADMIN", user, Organization);
        _api.Iam.Change(user, "ADMIN", Guid.NewGuid());

        (await _api.Client(token).GetAsync(OrganizationTrips, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IamAnswersAreCached_PerToken()
    {
        var token = _api.ActiveAccount("DRIVER", organizationId: Organization);
        var client = _api.Client(token);

        for (var i = 0; i < 3; i++)
            (await client.GetAsync(DriverAgenda, Ct)).IsSuccessStatusCode.ShouldBeTrue();

        _api.Iam.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task WithoutCache_ARevokedAccountLosesAccessOnTheNextRequest()
    {
        using var api = new SecurityApiFactory(new Dictionary<string, string?> { ["Iam:AccountValidation:CacheDuration"] = "00:00:00" });
        var driver = Guid.NewGuid();
        var token = api.ActiveAccount("DRIVER", driver, Organization);
        var client = api.Client(token);
        (await client.GetAsync(DriverAgenda, Ct)).IsSuccessStatusCode.ShouldBeTrue();

        api.Iam.Respond(driver, HttpStatusCode.Forbidden); // Iam suspendió la organización

        (await client.GetAsync(DriverAgenda, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IamUnavailable_ByDefaultTheValidatedJwtIsEnough(bool iamDown)
    {
        var token = _api.ActiveAccount("DRIVER", organizationId: Organization);
        _api.Iam.Down = iamDown;
        _api.Iam.Failing = !iamDown;

        var response = await _api.Client(token).PostAsJsonAsync($"/api/v1/trips/{Guid.NewGuid()}/sos",
            new { clientEventId = Guid.NewGuid() }, Ct);

        response.IsSuccessStatusCode.ShouldBeTrue("Un SOS no puede depender de que el monolito esté arriba");
        _api.Iam.Calls.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IamUnavailable_WithDenyReturns503(bool iamDown)
    {
        using var api = new SecurityApiFactory(new Dictionary<string, string?> { ["Iam:AccountValidation:WhenUnavailable"] = "Deny" });
        var token = api.ActiveAccount("DRIVER", organizationId: Organization);
        api.Iam.Down = iamDown;
        api.Iam.Failing = !iamDown;

        var response = await api.Client(token).GetAsync(DriverAgenda, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull();
        (await response.ProblemCodeAsync()).ShouldBe("IAM_UNAVAILABLE");
        api.Dispatcher.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task ValidationDisabled_OnlyTheJwtIsChecked()
    {
        using var api = new SecurityApiFactory(new Dictionary<string, string?>
        {
            ["Iam:AccountValidation:Enabled"] = "false",
            ["Iam:BaseUrl"] = ""
        });
        var token = IamTokens.Create(Guid.NewGuid(), "DRIVER", Organization); // cuenta desconocida para Iam

        (await api.Client(token).GetAsync(DriverAgenda, Ct)).IsSuccessStatusCode.ShouldBeTrue();
        api.Iam.Calls.ShouldBe(0);
    }

    // ───────────────────────── Canal en tiempo real (SignalR) ─────────────────────────

    [Fact]
    public async Task Hub_RequiresAnIamTokenInTheQueryString()
    {
        var anonymous = await _api.Client().PostAsync("/hubs/trip-tracking/negotiate?negotiateVersion=1", null, Ct);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var token = _api.ActiveAccount("PARENT", organizationId: null);
        var negotiate = await _api.Client().PostAsync(
            $"/hubs/trip-tracking/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(token)}", null, Ct);
        negotiate.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Hub_RejectsAccountsThatIamNoLongerAccepts()
    {
        var parent = Guid.NewGuid();
        var token = _api.ActiveAccount("PARENT", parent, organizationId: null);
        _api.Iam.Respond(parent, HttpStatusCode.NotFound);

        var negotiate = await _api.Client().PostAsync(
            $"/hubs/trip-tracking/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(token)}", null, Ct);

        negotiate.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ───────────────────────── Configuración: falla al arrancar ─────────────────────────

    [Theory]
    [InlineData("Jwt:Secret", "corto")]
    [InlineData("Jwt:Secret", "")]
    [InlineData("Jwt:Issuer", "")]
    [InlineData("Jwt:Audience", "")]
    [InlineData("Iam:BaseUrl", "")]
    [InlineData("Iam:BaseUrl", "no-es-una-url")]
    public void WeakOrMissingConfiguration_PreventsTheServiceFromStarting(string key, string value)
    {
        using var api = new SecurityApiFactory(new Dictionary<string, string?> { [key] = value });

        Should.Throw<InvalidOperationException>(() => api.CreateClient());
    }
}
