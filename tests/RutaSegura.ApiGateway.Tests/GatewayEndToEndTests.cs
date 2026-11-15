using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RutaSegura.ApiGateway.Tests.Support;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Application.Trips.Queries;

namespace RutaSegura.ApiGateway.Tests;

/// <summary>
/// La app usa una sola URL (la del Gateway). Detrás están el monolito real (Iam + Administration) y Trip &amp; Tracking
/// real, que valida los tokens de Iam y le pregunta a Iam por la cuenta directamente (no a través del Gateway).
/// </summary>
[Trait("Category", "Gateway")]
public sealed class GatewayEndToEndTests : IDisposable
{
    private readonly IamHost _iam = new();
    private readonly TripTrackingHost _tripTracking;
    private readonly GatewayHost _gateway = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GatewayEndToEndTests()
    {
        _tripTracking = new TripTrackingHost(_iam);
        _gateway.Network.Attach(Upstreams.Monolith, _iam.Server.CreateHandler());
        _gateway.Network.Attach(Upstreams.TripTracking, _tripTracking.Server.CreateHandler());
    }

    public void Dispose()
    {
        _gateway.Dispose();
        _tripTracking.Dispose();
        _iam.Dispose();
    }

    private async Task<string> SignInThroughGatewayAsync(string email, string password)
    {
        var response = await _gateway.Client().PostAsJsonAsync("/api/v1/users/sign-in", new { email, password }, GatewayHost.Json, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(GatewayHost.Json, Ct);
        return body.GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task DriverApp_UsesOnlyTheGateway()
    {
        var token = await SignInThroughGatewayAsync(SeedAccounts.DriverEmail, SeedAccounts.DriverPassword);
        var app = _gateway.Client(token);

        // Iam: validar la sesión al abrir la app.
        var me = await app.GetFromJsonAsync<JsonElement>("/api/v1/users/me", GatewayHost.Json, Ct);
        me.GetProperty("roleTier").GetString().ShouldBe("DRIVER");

        // Administration: la cuenta existe en Iam pero el ADMIN aún no la registró en el Roster.
        var profile = await app.GetAsync("/api/v1/roster/drivers/me", Ct);
        profile.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Trip & Tracking: agenda y SOS con el mismo token.
        (await app.GetAsync("/api/v1/drivers/me/trips", Ct)).IsSuccessStatusCode.ShouldBeTrue();
        (await app.PostAsJsonAsync($"/api/v1/trips/{Guid.NewGuid()}/sos", new { clientEventId = Guid.NewGuid() }, Ct))
            .IsSuccessStatusCode.ShouldBeTrue();

        _tripTracking.Dispatcher.Last<GetDriverAgendaQuery>().DriverId.ShouldBe(SeedAccounts.Driver);
        _tripTracking.Dispatcher.Last<RaiseSosCommand>().DriverId.ShouldBe(SeedAccounts.Driver);
    }

    [Fact]
    public async Task ParentApp_RegistersAndEnrollsAChild_OnlyThroughTheGateway()
    {
        var admin = _gateway.Client(await SignInThroughGatewayAsync(SeedAccounts.AdminEmail, SeedAccounts.AdminPassword));
        var parent = _gateway.Client(await SignInThroughGatewayAsync(SeedAccounts.ParentEmail, SeedAccounts.ParentPassword));

        // La empresa crea la ruta y comparte el código con las familias.
        var created = await admin.PostAsJsonAsync("/api/v1/routes", new
        {
            name = "Ruta Gateway",
            direction = "PICKUP",
            departureTime = "06:45:00",
            estimatedArrivalTime = "07:50:00",
            timeZoneId = "America/Lima",
            operatingDays = new[] { "Monday", "Friday" }
        }, GatewayHost.Json, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var route = await created.Content.ReadFromJsonAsync<JsonElement>(GatewayHost.Json, Ct);
        var code = route.GetProperty("enrollmentCode").GetString()!;

        // El padre registra a su hija (nombre, recojo) y sube su foto (multipart pasa por YARP).
        var child = await parent.PostAsJsonAsync("/api/v1/stakeholders/me/students", new
        {
            firstName = "Valeria",
            lastName = "Quispe",
            pickupLocation = new { address = "Av. Primavera 1240, Surco", latitude = -12.11234, longitude = -76.98567 }
        }, GatewayHost.Json, Ct);
        child.StatusCode.ShouldBe(HttpStatusCode.Created);
        var childId = (await child.Content.ReadFromJsonAsync<JsonElement>(GatewayHost.Json, Ct)).GetProperty("id").GetGuid();
        using var form = new MultipartFormDataContent();
        var png = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]);
        png.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(png, "photo", "foto.png");
        (await parent.PutAsync($"/api/v1/stakeholders/me/students/{childId}/photo", form, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await parent.GetAsync($"/api/v1/stakeholders/route-offers/{code}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var enrolled = await parent.PostAsJsonAsync($"/api/v1/stakeholders/me/students/{childId}/enrollments",
            new { enrollmentCode = code }, GatewayHost.Json, Ct);
        enrolled.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Route Planning creó la parada en la dirección de recojo.
        var planned = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/routes/{route.GetProperty("id").GetGuid()}",
            GatewayHost.Json, Ct);
        planned.GetProperty("stops")[0].GetProperty("studentIds")[0].GetGuid().ShouldBe(childId);
        planned.GetProperty("stops")[0].GetProperty("origin").GetString().ShouldBe("ENROLLMENT");
    }

    [Fact]
    public async Task Administrator_ManagesRoutesThroughTheGateway()
    {
        var token = await SignInThroughGatewayAsync(SeedAccounts.AdminEmail, SeedAccounts.AdminPassword);

        (await _gateway.Client(token).GetAsync("/api/v1/routes", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _gateway.Client(token).GetAsync("/api/v1/operations/alerts", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _gateway.Client(token).GetAsync("/api/v1/trips", Ct)).IsSuccessStatusCode.ShouldBeTrue();
        (await _gateway.Client(token).GetAsync($"/api/v1/organizations/{SeedAccounts.Organization}", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ServiceErrors_ReachTheAppUnchanged()
    {
        var wrongPassword = await _gateway.Client().PostAsJsonAsync("/api/v1/users/sign-in",
            new { email = SeedAccounts.DriverEmail, password = "Incorrecta999" }, GatewayHost.Json, Ct);
        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var withoutToken = await _gateway.Client().GetAsync("/api/v1/drivers/me/trips", Ct);
        withoutToken.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await withoutToken.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("UNAUTHORIZED");

        var parent = await SignInThroughGatewayAsync(SeedAccounts.ParentEmail, SeedAccounts.ParentPassword);
        var forbidden = await _gateway.Client(parent).GetAsync("/api/v1/drivers/me/trips", Ct);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await forbidden.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("FORBIDDEN");
    }

    [Fact]
    public async Task RealtimeHub_NegotiatesThroughTheGateway()
    {
        var token = await SignInThroughGatewayAsync(SeedAccounts.ParentEmail, SeedAccounts.ParentPassword);

        var negotiate = await _gateway.Client().PostAsync(
            $"/hubs/trip-tracking/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(token)}", null, Ct);

        negotiate.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await negotiate.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("connectionToken").GetString()
            .ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task OrganizationProvisioning_IsNotPublic()
    {
        var body = new
        {
            name = "Empresa por Gateway",
            administrator = new { firstName = "A", lastName = "B", email = "a@gateway.pe", password = "Password123" }
        };
        var throughGateway = _gateway.Client();
        throughGateway.DefaultRequestHeaders.Add("X-Provisioning-Key", SharedSecurity.ProvisioningKey);

        (await throughGateway.PostAsJsonAsync("/api/v1/organizations", body, GatewayHost.Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Readiness_ChecksBothServices()
    {
        (await _gateway.Client().GetAsync("/health/ready", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
