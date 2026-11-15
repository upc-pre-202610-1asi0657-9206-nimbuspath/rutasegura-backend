using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Monolith.Administration.Application.Internal.OutboundServices;
using Monolith.Administration.Infrastructure.Maps;
using RutaSegura.Monolith.Administration.Tests.Support;
namespace RutaSegura.Monolith.Administration.Tests.Infrastructure;
public sealed class OpenRouteServiceAdapterTests
{
    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestBody; public string? Key;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(ct); Key = request.Headers.GetValues("Authorization").Single();
            return new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
    [Fact]
    public async Task UsesLongitudeLatitudeAndMapsProviderGeometryAndSummary()
    {
        using var handler = new Handler(HttpStatusCode.OK, """{"features":[{"geometry":{"type":"LineString","coordinates":[[-76.98,-12.11],[-76.99,-12.12]]},"properties":{"summary":{"distance":650,"duration":120}}}]}""");
        using var client = new HttpClient(handler) { BaseAddress = new("https://maps.test/") };
        var adapter = new OpenRouteServiceAdapter(client, Options.Create(new OpenRouteServiceOptions { ApiKey = "fixture-key" }), TimeProvider.System);
        var result = await adapter.CalculateAsync([new(-12.11, -76.98), new(-12.12, -76.99)], TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        json.RootElement.GetProperty("coordinates")[0][0].GetDouble().ShouldBe(-76.98);
        handler.Key.ShouldBe("fixture-key"); result.DistanceMeters.ShouldBe(650); result.DurationSeconds.ShouldBe(120);
    }
    [Theory]
    [InlineData(429, "{}")]
    [InlineData(503, "private provider diagnostic")]
    [InlineData(200, "{invalid")]
    [InlineData(200, "{\"features\":[]}")]
    public async Task ProviderFailuresNeverBecomeFakeRoutesOrLeakedDiagnostics(int status, string body)
    {
        using var handler = new Handler((HttpStatusCode)status, body);
        using var client = new HttpClient(handler) { BaseAddress = new("https://maps.test/") };
        var adapter = new OpenRouteServiceAdapter(client, Options.Create(new OpenRouteServiceOptions { ApiKey = "secret-fixture-key" }), TimeProvider.System);
        var error = await Should.ThrowAsync<MapProviderException>(() => adapter.CalculateAsync([new(-12.11, -76.98), new(-12.12, -76.99)], TestContext.Current.CancellationToken));
        error.Message.ShouldStartWith("MAP_PROVIDER_"); error.Message.ShouldNotContain("secret-fixture-key"); error.Message.ShouldNotContain("private");
    }
    [Fact]
    public void MovingOrReorderingStopsInvalidatesCalculatedGeometry()
    {
        var route = AdministrationMother.NewRoute();
        var first = route.AddStop("Uno", null, AdministrationMother.StopLocation(0));
        var second = route.AddStop("Dos", null, AdministrationMother.StopLocation(1));
        route.SetItinerary(new("{\"type\":\"LineString\",\"coordinates\":[[0,0],[1,1]]}", 10, 20, DateTimeOffset.UtcNow));
        var version = route.GeometryVersion;
        route.ReorderStops([second.Id, first.Id]); route.Itinerary.ShouldBeNull(); route.GeometryVersion.ShouldNotBe(version);
    }
}
