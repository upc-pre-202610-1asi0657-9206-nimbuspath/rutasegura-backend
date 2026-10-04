using RutaSegura.TripTracking.Domain.Common;

namespace RutaSegura.TripTracking.Domain.Tracking;

/// <summary>
/// Punto aceptado del recorrido. Es append-only y se persiste fuera del agregado Trip
/// (tabla de telemetría) para no reescribir el viaje en cada ping (AC-03).
/// </summary>
public sealed record TrackPoint(
    TripId TripId,
    GeoPosition Position,
    DateTimeOffset RecordedAt,
    DateTimeOffset ReceivedAt,
    double AccuracyMeters,
    double? SpeedKmh,
    double? HeadingDegrees);

public enum PositionRecordingOutcome
{
    Accepted,
    IgnoredStaleOrDuplicate,
    IgnoredBeforeTripStart,
    IgnoredFutureTimestamp,
    IgnoredLowAccuracy,
    IgnoredOutlier
}

public sealed record PositionRecordingResult(PositionRecordingOutcome Outcome, TrackPoint? Point)
{
    public bool Accepted => Outcome == PositionRecordingOutcome.Accepted;

    internal static PositionRecordingResult Ignored(PositionRecordingOutcome outcome) => new(outcome, null);
}
