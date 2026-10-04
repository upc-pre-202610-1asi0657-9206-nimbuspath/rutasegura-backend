using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Application.Validation;
using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Trips.Commands;

/// <summary>Lectura GPS cruda tal como la envía el celular.</summary>
public sealed record GpsReadingInput(
    double Latitude,
    double Longitude,
    DateTimeOffset RecordedAt,
    double AccuracyMeters,
    double? SpeedKmh = null,
    double? HeadingDegrees = null);

/// <summary>
/// Lote de posiciones del conductor. Online llega 1 lectura cada ~3-5 s; tras perder señal,
/// la app envía en un solo lote todo lo acumulado (Store-and-Forward, PT-09).
/// </summary>
public sealed record RecordPositionsCommand(Guid TripId, Guid DriverId, IReadOnlyList<GpsReadingInput> Readings)
    : ICommand<RecordPositionsResult>;

public sealed record RecordPositionsResult(int Accepted, int Ignored, int Rejected, DateTimeOffset? LastAcceptedAt);

internal sealed class RecordPositionsCommandValidator : IValidator<RecordPositionsCommand>
{
    public const int MaxBatchSize = 500;

    public IEnumerable<Error> Validate(RecordPositionsCommand command)
    {
        if (command.Readings is null || command.Readings.Count == 0)
            yield return Error.Validation("VALIDATION_READINGS_REQUIRED", "Envía al menos una lectura GPS.");
        else if (command.Readings.Count > MaxBatchSize)
            yield return Error.Validation("VALIDATION_BATCH_TOO_LARGE", $"Máximo {MaxBatchSize} lecturas por lote.");
    }
}

internal sealed class RecordPositionsCommandHandler(
    ITripRepository trips,
    ITrackPointRepository trackPoints,
    ILivePositionPublisher livePublisher,
    TimeProvider clock) : ICommandHandler<RecordPositionsCommand, RecordPositionsResult>
{
    public async Task<Result<RecordPositionsResult>> HandleAsync(RecordPositionsCommand command, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadForDriverAsync(command.TripId, command.DriverId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;

        var driverId = new DriverId(command.DriverId);
        var receivedAt = clock.GetUtcNow();
        var accepted = new List<TrackPoint>();
        int ignored = 0, rejected = 0;

        foreach (var input in command.Readings.OrderBy(r => r.RecordedAt))
        {
            var reading = TryCreateReading(input);
            if (reading is null)
            {
                rejected++;
                continue;
            }

            // Si el viaje no está activo, el dominio lanza TRACKING_REQUIRES_ACTIVE_TRIP para todo el lote.
            var result = trip.RecordPosition(driverId, reading, receivedAt);
            if (result.Accepted) accepted.Add(result.Point!);
            else ignored++;
        }

        if (accepted.Count > 0)
        {
            await trackPoints.AddRangeAsync(accepted, cancellationToken);
            await livePublisher.PublishAsync(BuildLiveUpdate(trip), cancellationToken);
        }

        return new RecordPositionsResult(accepted.Count, ignored, rejected, accepted.LastOrDefault()?.RecordedAt);
    }

    private static GpsReading? TryCreateReading(GpsReadingInput input)
    {
        try
        {
            return GpsReading.Create(
                GeoPosition.Create(input.Latitude, input.Longitude),
                input.RecordedAt, input.AccuracyMeters, input.SpeedKmh, input.HeadingDegrees);
        }
        catch (DomainException ex) when (ex.Code is TrackingErrors.InvalidCoordinates or TrackingErrors.InvalidReading)
        {
            return null; // lectura corrupta: se descarta sin tumbar el lote
        }
    }

    internal static LivePositionUpdate BuildLiveUpdate(Trip trip)
    {
        var position = trip.LastPosition!;
        var next = trip.NextStop;
        double? distance = next is null ? null : Math.Round(next.Position.DistanceMetersTo(position));

        return new LivePositionUpdate(
            trip.Id.Value,
            trip.RouteId.Value,
            trip.VehicleId.Value,
            trip.Status.ToString(),
            position.Latitude,
            position.Longitude,
            trip.LastPositionAt!.Value,
            trip.LastSpeedKmh,
            trip.LastHeadingDegrees,
            next?.StopId.Value,
            distance,
            distance is null ? null : EtaEstimator.EstimateMinutes(distance.Value, trip.LastSpeedKmh),
            trip.PassengersOnBoard.Count());
    }
}
