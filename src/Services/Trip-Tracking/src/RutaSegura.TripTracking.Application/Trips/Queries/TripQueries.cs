using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Domain.Access;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Trips.Queries;

// ───────────────────────── DTOs de lectura ─────────────────────────

public sealed record LivePositionDto(
    double Latitude, double Longitude, DateTimeOffset RecordedAt, double? SpeedKmh, double? HeadingDegrees, int AgeSeconds);

public sealed record NextStopDto(Guid StopId, string Name, int Sequence, int TotalStops, double? DistanceMeters, int? EtaMinutes);

public sealed record PassengerDto(
    Guid StudentId, Guid StopId, string Status, DateTimeOffset? BoardedAt, DateTimeOffset? AlightedAt,
    string? AbsenceSource, string? AbsenceNote);

public sealed record TripLiveStatusDto(
    Guid TripId,
    string RouteName,
    string Direction,
    string Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? StartedAt,
    LivePositionDto? Position,
    NextStopDto? NextStop,
    int StudentsOnBoard,
    int TotalStudents,
    bool HasActiveSos,
    IReadOnlyList<PassengerDto> Passengers);

// ───────────────────────── Viaje en vivo (tutor / conductor / empresa) ─────────────────────────

public sealed record GetTripLiveStatusQuery(Guid TripId, Requester Requester) : IQuery<TripLiveStatusDto>;

internal sealed class GetTripLiveStatusQueryHandler(ITripRepository trips, IGuardianDirectory guardians, TimeProvider clock)
    : IQueryHandler<GetTripLiveStatusQuery, TripLiveStatusDto>
{
    public async Task<Result<TripLiveStatusDto>> HandleAsync(GetTripLiveStatusQuery query, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadAsync(query.TripId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;

        IReadOnlyList<TripPassenger> visiblePassengers;
        switch (query.Requester.Role)
        {
            case RequesterRole.Guardian:
                var children = await guardians.GetStudentsOfGuardianAsync(new GuardianId(query.Requester.UserId), cancellationToken);
                if (!GuardianAccessPolicy.CanViewTrip(trip, children)) return ApplicationErrors.TripAccessDenied;
                visiblePassengers = GuardianAccessPolicy.VisiblePassengers(trip, children);
                break;
            case RequesterRole.Driver:
                if (!trip.IsOperatedBy(new DriverId(query.Requester.UserId))) return ApplicationErrors.TripAccessDenied;
                visiblePassengers = trip.Passengers;
                break;
            case RequesterRole.Admin:
                visiblePassengers = trip.Passengers; // TODO Etapa 5: acotar a la empresa del admin (claim company_id)
                break;
            default:
                return ApplicationErrors.TripAccessDenied;
        }

        return new TripLiveStatusDto(
            trip.Id.Value,
            trip.RouteName,
            trip.Direction.ToString(),
            trip.Status.ToString(),
            trip.ScheduledStart,
            trip.StartedAt,
            BuildPosition(trip, clock.GetUtcNow()),
            BuildNextStop(trip),
            trip.PassengersOnBoard.Count(),
            trip.Passengers.Count,
            trip.ActiveSos is not null,
            visiblePassengers.Select(ToDto).ToList());
    }

    private static LivePositionDto? BuildPosition(Trip trip, DateTimeOffset now)
    {
        // "Tu ubicación se comparte solo mientras un viaje está en curso."
        if (!GuardianAccessPolicy.CanSeeLivePosition(trip) || trip.LastPosition is null) return null;

        var age = (int)Math.Max(0, (now - trip.LastPositionAt!.Value).TotalSeconds);
        return new LivePositionDto(trip.LastPosition.Latitude, trip.LastPosition.Longitude, trip.LastPositionAt.Value,
            trip.LastSpeedKmh, trip.LastHeadingDegrees, age);
    }

    private static NextStopDto? BuildNextStop(Trip trip)
    {
        var next = trip.NextStop;
        if (next is null || !trip.IsActive) return null;

        double? distance = trip.LastPosition is null ? null : Math.Round(next.Position.DistanceMetersTo(trip.LastPosition));
        int? eta = distance is null ? null : EtaEstimator.EstimateMinutes(distance.Value, trip.LastSpeedKmh);
        return new NextStopDto(next.StopId.Value, next.Name, next.Sequence, trip.Stops.Count, distance, eta);
    }

    internal static PassengerDto ToDto(TripPassenger p) => new(
        p.StudentId.Value, p.StopId.Value, p.Status.ToString(), p.BoardedAt, p.AlightedAt,
        p.AbsenceSource?.ToString(), p.AbsenceNote);
}

// ───────────────────────── Resumen al finalizar (pantalla "Viaje finalizado") ─────────────────────────

public sealed record IncidentDto(Guid IncidentId, string Type, string? Detail, DateTimeOffset OccurredAt);

public sealed record TripSummaryDto(
    Guid TripId, string RouteName, string Direction, string Status,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    int Delivered, IReadOnlyList<PassengerDto> Absent, IReadOnlyList<IncidentDto> Incidents);

public sealed record GetTripSummaryQuery(Guid TripId, Requester Requester) : IQuery<TripSummaryDto>;

internal sealed class GetTripSummaryQueryHandler(ITripRepository trips) : IQueryHandler<GetTripSummaryQuery, TripSummaryDto>
{
    public async Task<Result<TripSummaryDto>> HandleAsync(GetTripSummaryQuery query, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadAsync(query.TripId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;

        var allowed = query.Requester.Role == RequesterRole.Admin ||
                      (query.Requester.Role == RequesterRole.Driver && trip.IsOperatedBy(new DriverId(query.Requester.UserId)));
        if (!allowed) return ApplicationErrors.TripAccessDenied;

        return new TripSummaryDto(
            trip.Id.Value, trip.RouteName, trip.Direction.ToString(), trip.Status.ToString(),
            trip.StartedAt, trip.CompletedAt,
            trip.Passengers.Count(p => p.Status == PassengerStatus.Alighted),
            trip.Passengers.Where(p => p.Status == PassengerStatus.Absent).Select(GetTripLiveStatusQueryHandler.ToDto).ToList(),
            trip.Incidents.OrderBy(i => i.OccurredAt)
                .Select(i => new IncidentDto(i.Id.Value, i.Type.ToString(), i.Detail, i.OccurredAt)).ToList());
    }
}

// ───────────────────────── Agenda del conductor (pantalla de inicio) ─────────────────────────

public sealed record DriverTripDto(
    Guid TripId, string RouteName, string Direction, DateTimeOffset ScheduledStart, string Status,
    Guid VehicleId, int StopCount, int StudentCount);

public sealed record GetDriverAgendaQuery(Guid DriverId, DateTimeOffset From, DateTimeOffset To) : IQuery<IReadOnlyList<DriverTripDto>>;

internal sealed class GetDriverAgendaQueryHandler(ITripRepository trips) : IQueryHandler<GetDriverAgendaQuery, IReadOnlyList<DriverTripDto>>
{
    public async Task<Result<IReadOnlyList<DriverTripDto>>> HandleAsync(GetDriverAgendaQuery query, CancellationToken cancellationToken)
    {
        var list = await trips.ListByDriverAsync(new DriverId(query.DriverId), query.From, query.To, cancellationToken);

        IReadOnlyList<DriverTripDto> agenda = list
            .Where(t => t.Status != TripStatus.Cancelled)
            .OrderBy(t => t.ScheduledStart)
            .Select(t => new DriverTripDto(
                t.Id.Value, t.RouteName, t.Direction.ToString(), t.ScheduledStart, t.Status.ToString(),
                t.VehicleId.Value, t.Stops.Count, t.Passengers.Count(p => p.Status != PassengerStatus.Absent)))
            .ToList();

        return Result<IReadOnlyList<DriverTripDto>>.Success(agenda);
    }
}
