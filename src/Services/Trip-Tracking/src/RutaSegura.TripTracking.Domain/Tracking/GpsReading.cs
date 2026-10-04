using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Domain.Common;

namespace RutaSegura.TripTracking.Domain.Tracking;

/// <summary>
/// Lectura cruda enviada por el celular del conductor.
/// <see cref="RecordedAt"/> es la hora del dispositivo (importa para lotes offline).
/// </summary>
public sealed record GpsReading
{
    public GeoPosition Position { get; }
    public DateTimeOffset RecordedAt { get; }
    public double AccuracyMeters { get; }
    public double? SpeedKmh { get; }
    public double? HeadingDegrees { get; }

    private GpsReading(GeoPosition position, DateTimeOffset recordedAt, double accuracyMeters, double? speedKmh, double? headingDegrees)
    {
        Position = position;
        RecordedAt = recordedAt;
        AccuracyMeters = accuracyMeters;
        SpeedKmh = speedKmh;
        HeadingDegrees = headingDegrees;
    }

    public static GpsReading Create(
        GeoPosition position,
        DateTimeOffset recordedAt,
        double accuracyMeters,
        double? speedKmh = null,
        double? headingDegrees = null)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (double.IsNaN(accuracyMeters) || accuracyMeters < 0)
            throw new DomainException(TrackingErrors.InvalidReading, "La precisión debe ser un número positivo.");

        if (speedKmh is { } speed && (double.IsNaN(speed) || speed < 0))
            throw new DomainException(TrackingErrors.InvalidReading, "La velocidad no puede ser negativa.");

        if (headingDegrees is { } heading && (double.IsNaN(heading) || heading is < 0 or >= 360))
            throw new DomainException(TrackingErrors.InvalidReading, "El rumbo debe estar entre 0 y 359.99 grados.");

        return new GpsReading(position, recordedAt, accuracyMeters, speedKmh, headingDegrees);
    }
}
