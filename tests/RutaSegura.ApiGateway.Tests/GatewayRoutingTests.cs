using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RutaSegura.ApiGateway.Tests.Support;

namespace RutaSegura.ApiGateway.Tests;

/// <summary>
/// El Gateway como punto de entrada único: cada ruta pública llega a su servicio sin cambios (ruta, query, cuerpo y
/// token), lo interno nunca se expone y los errores propios del Gateway salen como ProblemDetails con <c>code</c>.
/// </summary>
[Trait("Category", "Gateway")]
public sealed class GatewayRoutingTests : IAsyncLifetime
{
    private GatewayHost _gateway = null!;
    private EchoService _monolith = null!;
    private EchoService _tripTracking = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _monolith = await EchoService.StartAsync("monolith");
        _tripTracking = await EchoService.StartAsync("trip-tracking");
        _gateway = new GatewayHost();
        _gateway.Network.Attach(Upstreams.Monolith, _monolith.Handler);
        _gateway.Network.Attach(Upstreams.TripTracking, _tripTracking.Handler);
    }

    public async ValueTask DisposeAsync()
    {
        await _gateway.DisposeAsync();
        await _monolith.DisposeAsync();
        await _tripTracking.DisposeAsync();
    }

    [Theory]
    // Iam (monolito)
    [InlineData("POST", "/api/v1/users/sign-in", "monolith")]
    [InlineData("POST", "/api/v1/users", "monolith")]
    [InlineData("GET", "/api/v1/users/me", "monolith")]
    [InlineData("POST", "/api/v1/users/drivers", "monolith")]
    [InlineData("GET", "/api/v1/organizations/0a000000-0000-0000-0000-000000000001", "monolith")]
    [InlineData("PUT", "/api/v1/organizations/0a000000-0000-0000-0000-000000000001", "monolith")]
    // Administration (monolito)
    [InlineData("GET", "/api/v1/roster/drivers/me", "monolith")]
    [InlineData("GET", "/api/v1/roster/drivers/me/routes", "monolith")]
    [InlineData("POST", "/api/v1/roster/vehicles", "monolith")]
    [InlineData("GET", "/api/v1/routes", "monolith")]
    [InlineData("POST", "/api/v1/routes/7b1d3c55-1111-4c3b-9a5b-000000000001/activate", "monolith")]
    [InlineData("GET", "/api/v1/operations/alerts", "monolith")]
    // Stakeholder (monolito)
    [InlineData("GET", "/api/v1/stakeholders/me/students", "monolith")]
    [InlineData("POST", "/api/v1/stakeholders/me/students/7b1d3c55-1111-4c3b-9a5b-000000000001/enrollments", "monolith")]
    [InlineData("PUT", "/api/v1/stakeholders/me/students/7b1d3c55-1111-4c3b-9a5b-000000000001/pickup-location", "monolith")]
    [InlineData("GET", "/api/v1/stakeholders/route-offers/K7QM4P2X", "monolith")]
    [InlineData("GET", "/api/v1/stakeholders/drivers/me/students", "monolith")]
    [InlineData("GET", "/api/v1/stakeholders/students/7b1d3c55-1111-4c3b-9a5b-000000000001/photo", "monolith")]
    // Trip & Tracking
    [InlineData("GET", "/api/v1/drivers/me/trips", "trip-tracking")]
    [InlineData("GET", "/api/v1/guardians/me/trips", "trip-tracking")]
    [InlineData("GET", "/api/v1/trips", "trip-tracking")]
    [InlineData("POST", "/api/v1/trips/7b1d3c55-1111-4c3b-9a5b-000000000001/start", "trip-tracking")]
    [InlineData("GET", "/api/v1/trips/7b1d3c55-1111-4c3b-9a5b-000000000001/live", "trip-tracking")]
    [InlineData("POST", "/api/v1/trips/7b1d3c55-1111-4c3b-9a5b-000000000001/sync", "trip-tracking")]
    [InlineData("POST", "/api/v1/tracking/positions", "trip-tracking")]
    [InlineData("POST", "/hubs/trip-tracking/negotiate", "trip-tracking")]
    public async Task PublicRoute_ReachesItsServiceUnchanged(string method, string path, string service)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path + "?negotiateVersion=1&from=2026-10-26");
        if (method is "POST" or "PUT") request.Content = JsonContent.Create(new { clientEventId = "3b0e7c1a-0000-0000-0000-000000000001" });
        request.Headers.Authorization = new("Bearer", "token-de-iam");

        var response = await _gateway.Client().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var echo = (await response.Content.ReadFromJsonAsync<EchoResponse>(GatewayHost.Json, Ct))!;
        echo.Service.ShouldBe(service);
        echo.Method.ShouldBe(method);
        echo.Path.ShouldBe(path);
        echo.Query.ShouldBe("?negotiateVersion=1&from=2026-10-26");
        echo.Authorization.ShouldBe("Bearer token-de-iam", "El Gateway reenvía el token de Iam tal cual");
        if (method is "POST" or "PUT") echo.Body.ShouldContain("3b0e7c1a-0000-0000-0000-000000000001");
    }

    [Theory]
    [InlineData("GET", "/internal/v1/trips/7b1d3c55-1111-4c3b-9a5b-000000000001/notification-context")]
    [InlineData("POST", "/dev/tokens")]
    [InlineData("GET", "/dev/demo-data")]
    [InlineData("GET", "/swagger/index.html")]
    [InlineData("GET", "/openapi/v1.json")]
    [InlineData("GET", "/health")]
    [InlineData("POST", "/api/v1/organizations")] // provisioning con X-Provisioning-Key: solo plataforma
    [InlineData("DELETE", "/api/v1/organizations/0a000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/v1/no-existe")]
    public async Task NonPublicRoute_IsNotForwarded(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Provisioning-Key", "cualquiera");

        var response = await _gateway.Client().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Code(response)).ShouldBe("GATEWAY_ROUTE_NOT_FOUND");
    }

    [Fact]
    public async Task NotificationDisabled_Answers503WithoutCallingIt()
    {
        var response = await _gateway.Client("token").GetAsync("/api/v1/notifications", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await Code(response)).ShouldBe("SERVICE_DISABLED");
    }

    [Fact]
    public async Task CorrelationId_IsCreatedAndSentToTheServiceAndBack()
    {
        var response = await _gateway.Client().GetAsync("/api/v1/drivers/me/trips", Ct);

        var echo = (await response.Content.ReadFromJsonAsync<EchoResponse>(GatewayHost.Json, Ct))!;
        echo.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        response.Headers.GetValues("X-Correlation-Id").Single().ShouldBe(echo.CorrelationId);
    }

    [Fact]
    public async Task CorrelationId_FromTheApp_IsKept()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Add("X-Correlation-Id", "app-1234");

        var response = await _gateway.Client().SendAsync(request, Ct);

        (await response.Content.ReadFromJsonAsync<EchoResponse>(GatewayHost.Json, Ct))!.CorrelationId.ShouldBe("app-1234");
        response.Headers.GetValues("X-Correlation-Id").Single().ShouldBe("app-1234");
    }

    [Fact]
    public async Task ServicesReceiveForwardedHeaders()
    {
        var echo = await _gateway.Client().GetFromJsonAsync<EchoResponse>("/api/v1/routes", GatewayHost.Json, Ct);

        echo!.ForwardedProto.ShouldBe("http");
    }

    [Fact]
    public async Task ServiceDown_Returns502ProblemDetails()
    {
        _gateway.Network.TurnOff(Upstreams.TripTracking);

        var response = await _gateway.Client("token").PostAsJsonAsync("/api/v1/tracking/positions", new { }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await Code(response)).ShouldBe("UPSTREAM_UNAVAILABLE");
        // El otro servicio sigue respondiendo.
        (await _gateway.Client().GetAsync("/api/v1/users/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SignIn_IsRateLimitedPerClient()
    {
        await using var gateway = new GatewayHost(new Dictionary<string, string?>
        {
            ["Gateway:SignInRateLimit:PermitLimit"] = "3",
            ["Gateway:SignInRateLimit:Window"] = "00:10:00"
        });
        gateway.Network.Attach(Upstreams.Monolith, _monolith.Handler);
        var client = gateway.Client();

        for (var i = 0; i < 3; i++)
            (await client.PostAsJsonAsync("/api/v1/users/sign-in", new { email = "x@x.pe", password = "x" }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.OK);

        var blocked = await client.PostAsJsonAsync("/api/v1/users/sign-in", new { email = "x@x.pe", password = "x" }, Ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Code(blocked)).ShouldBe("TOO_MANY_REQUESTS");

        // Solo se limita el inicio de sesión / registro, no el resto de la API.
        (await client.GetAsync("/api/v1/users/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_LiveAlwaysAndReadyOnlyWithTheServicesUp()
    {
        (await _gateway.Client().GetAsync("/health/live", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _gateway.Client().GetAsync("/health/ready", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        _gateway.Network.TurnOff(Upstreams.Monolith);

        (await _gateway.Client().GetAsync("/health/live", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _gateway.Client().GetAsync("/health/ready", Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return body.GetProperty("code").GetString();
    }
}
