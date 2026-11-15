namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>A calculated road itinerary. Times are estimates, not a promise of arrival.</summary>
public sealed record RouteItinerary
{
    public RouteItinerary() { GeometryGeoJson = ""; }
    public RouteItinerary(string geometryGeoJson, double distanceMeters, double durationSeconds, DateTimeOffset calculatedAt)
    {
        if (string.IsNullOrWhiteSpace(geometryGeoJson) || geometryGeoJson.Length > 2_000_000 ||
            !double.IsFinite(distanceMeters) || distanceMeters < 0 || !double.IsFinite(durationSeconds) || durationSeconds < 0)
            throw new ArgumentException("Invalid calculated itinerary.");
        GeometryGeoJson = geometryGeoJson; DistanceMeters = distanceMeters; DurationSeconds = durationSeconds; CalculatedAt = calculatedAt;
    }
    public string GeometryGeoJson { get; init; }
    public double DistanceMeters { get; init; }
    public double DurationSeconds { get; init; }
    public DateTimeOffset CalculatedAt { get; init; }
}
