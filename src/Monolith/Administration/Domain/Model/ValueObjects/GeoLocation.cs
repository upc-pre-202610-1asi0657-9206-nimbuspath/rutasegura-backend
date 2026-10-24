namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>WGS-84 coordinates of a stop (where a child is picked up or dropped off).</summary>
public record GeoLocation
{
    private const double EarthRadiusMeters = 6_371_000;

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    public GeoLocation()
    {
    }

    public GeoLocation(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
            throw new ArgumentException("Latitude must be between -90 and 90.", nameof(latitude));
        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
            throw new ArgumentException("Longitude must be between -180 and 180.", nameof(longitude));
        if (latitude == 0 && longitude == 0)
            throw new ArgumentException("Coordinates (0, 0) are not a valid location.", nameof(latitude));

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    /// <summary>Great-circle distance (Haversine) in meters.</summary>
    public double DistanceInMetersTo(GeoLocation other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var dLat = ToRadians(other.Latitude - Latitude);
        var dLon = ToRadians(other.Longitude - Longitude);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(Latitude)) * Math.Cos(ToRadians(other.Latitude)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    public override string ToString() => $"({Latitude}, {Longitude})";
}
