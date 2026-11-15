using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Monolith.Administration.Application.Internal.OutboundServices;
using Monolith.Administration.Domain.Model.ValueObjects;
namespace Monolith.Administration.Infrastructure.Maps;
public sealed class OpenRouteServiceOptions
{
    public string BaseUrl { get; set; } = "https://api.openrouteservice.org/";
    public string ApiKey { get; set; } = "";
    public string Profile { get; set; } = "driving-car";
    public int TimeoutSeconds { get; set; } = 10;
}
/// <summary>ACL: ORS GeoJSON -> internal itinerary; never returns provider diagnostics or API keys.</summary>
public sealed class OpenRouteServiceAdapter(HttpClient client, IOptions<OpenRouteServiceOptions> settings, TimeProvider clock) : IMapProvider
{
    public async Task<RouteItinerary> CalculateAsync(IReadOnlyList<GeoLocation> stops, CancellationToken ct)
    {
        var options = settings.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey)) throw new MapProviderException("MAP_PROVIDER_NOT_CONFIGURED");
        if (options.Profile is not ("driving-car" or "driving-hgv")) throw new MapProviderException("MAP_PROVIDER_INVALID_CONFIGURATION");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v2/directions/{options.Profile}/geojson");
        request.Headers.TryAddWithoutValidation("Authorization", options.ApiKey);
        request.Content = JsonContent.Create(new { coordinates = stops.Select(s => new[] { s.Longitude, s.Latitude }), instructions = false });
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            if (!response.IsSuccessStatusCode) throw new MapProviderException("MAP_PROVIDER_UNAVAILABLE");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var feature = json.RootElement.GetProperty("features")[0];
            var geometry = feature.GetProperty("geometry");
            if (geometry.GetProperty("type").GetString() != "LineString") throw new JsonException();
            var points = geometry.GetProperty("coordinates");
            if (points.ValueKind != JsonValueKind.Array || points.GetArrayLength() is < 2 or > 100000) throw new JsonException();
            foreach (var point in points.EnumerateArray())
            {
                if (point.GetArrayLength() != 2) throw new JsonException();
                var lon = point[0].GetDouble(); var lat = point[1].GetDouble();
                if (!double.IsFinite(lon) || !double.IsFinite(lat) || lon is < -180 or > 180 || lat is < -90 or > 90) throw new JsonException();
            }
            var summary = feature.GetProperty("properties").GetProperty("summary");
            return new RouteItinerary(geometry.GetRawText(), summary.GetProperty("distance").GetDouble(), summary.GetProperty("duration").GetDouble(), clock.GetUtcNow());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new MapProviderException("MAP_PROVIDER_TIMEOUT"); }
        catch (HttpRequestException) { throw new MapProviderException("MAP_PROVIDER_UNAVAILABLE"); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or IndexOutOfRangeException)
        { throw new MapProviderException("MAP_PROVIDER_INVALID_RESPONSE"); }
    }
}
