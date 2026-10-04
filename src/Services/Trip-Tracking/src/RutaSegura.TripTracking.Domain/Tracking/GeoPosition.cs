using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Domain.Common;

namespace RutaSegura.TripTracking.Domain.Tracking;

/// <summary>Coordenada WGS84 validada (value object inmutable).</summary>
public sealed record GeoPosition
{
    private const double EarthRadiusMeters = 6_371_000d;

    public double Latitude { get; }
    public double Longitude { get; }

    private GeoPosition(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public static GeoPosition Create(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
            throw new DomainException(TrackingErrors.InvalidCoordinates, $"Latitud fuera de rango: {latitude}.");

        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
            throw new DomainException(TrackingErrors.InvalidCoordinates, $"Longitud fuera de rango: {longitude}.");

        // (0,0) "Null Island" es el valor por defecto que envían algunos GPS sin fix.
        if (latitude == 0 && longitude == 0)
            throw new DomainException(TrackingErrors.InvalidCoordinates, "Coordenada (0,0) inválida: el GPS no tiene señal.");

        return new GeoPosition(latitude, longitude);
    }

    /// <summary>Distancia ortodrómica (Haversine) en metros.</summary>
    public double DistanceMetersTo(GeoPosition other)
    {
        var dLat = ToRadians(other.Latitude - Latitude);
        var dLon = ToRadians(other.Longitude - Longitude);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(Latitude)) * Math.Cos(ToRadians(other.Latitude)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
