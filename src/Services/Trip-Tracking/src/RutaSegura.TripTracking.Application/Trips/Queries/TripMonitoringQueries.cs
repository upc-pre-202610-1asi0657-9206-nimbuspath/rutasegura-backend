using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Domain.Access;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Trips.Queries;

internal static class DateRange
{
    public const int MaxDays = 31;

    public static bool IsValid(DateTimeOffset from, DateTimeOffset to) => to > from && to - from <= TimeSpan.FromDays(MaxDays);
}

// ───────────────────────── Viajes de mis hijos (pantalla de inicio del tutor) ─────────────────────────

public sealed record GuardianTripDto(
    Guid TripId, string RouteName, string Direction, string Status, DateTimeOffset ScheduledStart, DateTimeOffset? StartedAt,
    bool IsLive, IReadOnlyList<PassengerDto> MyStudents);

public sealed record GetGuardianTripsQuery(Guid GuardianId, DateTimeOffset From, DateTimeOffset To) : IQuery<IReadOnlyList<GuardianTripDto>>;

internal sealed class GetGuardianTripsQueryHandler(ITripRepository trips, IGuardianDirectory guardians)
    : IQueryHandler<GetGuardianTripsQuery, IReadOnlyList<GuardianTripDto>>
{
    public async Task<Result<IReadOnlyList<GuardianTripDto>>> HandleAsync(GetGuardianTripsQuery query, CancellationToken cancellationToken)
    {
        if (!DateRange.IsValid(query.From, query.To)) return ApplicationErrors.InvalidDateRange(DateRange.MaxDays);

        var children = await guardians.GetStudentsOfGuardianAsync(new GuardianId(query.GuardianId), cancellationToken);
        if (children.Count == 0) return Result<IReadOnlyList<GuardianTripDto>>.Success([]);

        var list = await trips.ListByStudentsAsync(children, query.From, query.To, cancellationToken);

        IReadOnlyList<GuardianTripDto> result = list
            .Where(t => t.Status != TripStatus.Cancelled && GuardianAccessPolicy.CanViewTrip(t, children))
            .OrderBy(t => t.ScheduledStart)
            .Select(t => new GuardianTripDto(
                t.Id.Value, t.RouteName, t.Direction.ToString(), t.Status.ToString(), t.ScheduledStart, t.StartedAt,
                GuardianAccessPolicy.CanSeeLivePosition(t),
                GuardianAccessPolicy.VisiblePassengers(t, children).Select(GetTripLiveStatusQueryHandler.ToDto).ToList()))
            .ToList();

        return Result<IReadOnlyList<GuardianTripDto>>.Success(result);
    }
}

// ───────────────────────── Monitoreo de la empresa (panel del administrador) ─────────────────────────

public sealed record OrganizationTripDto(
    Guid TripId, Guid RouteId, string RouteName, string Direction, string Status,
    DateTimeOffset ScheduledStart, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    Guid DriverId, Guid VehicleId, int StudentsOnBoard, int TotalStudents, bool HasActiveSos, int IncidentCount,
    DateTimeOffset? LastPositionAt);

public sealed record GetOrganizationTripsQuery(Guid OrganizationId, DateTimeOffset From, DateTimeOffset To, TripStatus? Status = null)
    : IQuery<IReadOnlyList<OrganizationTripDto>>;

internal sealed class GetOrganizationTripsQueryHandler(ITripRepository trips)
    : IQueryHandler<GetOrganizationTripsQuery, IReadOnlyList<OrganizationTripDto>>
{
    public async Task<Result<IReadOnlyList<OrganizationTripDto>>> HandleAsync(GetOrganizationTripsQuery query, CancellationToken cancellationToken)
    {
        if (query.OrganizationId == Guid.Empty) return ApplicationErrors.OrganizationRequired;
        if (!DateRange.IsValid(query.From, query.To)) return ApplicationErrors.InvalidDateRange(DateRange.MaxDays);

        var list = await trips.ListByOrganizationAsync(new OrganizationId(query.OrganizationId), query.From, query.To, query.Status, cancellationToken);

        IReadOnlyList<OrganizationTripDto> result = list
            .OrderBy(t => t.ScheduledStart)
            .Select(t => new OrganizationTripDto(
                t.Id.Value, t.RouteId.Value, t.RouteName, t.Direction.ToString(), t.Status.ToString(),
                t.ScheduledStart, t.StartedAt, t.CompletedAt, t.DriverId.Value, t.VehicleId.Value,
                t.PassengersOnBoard.Count(), t.Passengers.Count, t.ActiveSos is not null, t.Incidents.Count, t.LastPositionAt))
            .ToList();

        return Result<IReadOnlyList<OrganizationTripDto>>.Success(result);
    }
}

// ───────────────────────── Recorrido registrado (auditoría / reportes) ─────────────────────────

public sealed record TrackPointDto(double Latitude, double Longitude, DateTimeOffset RecordedAt, double AccuracyMeters, double? SpeedKmh, double? HeadingDegrees);

public sealed record TripTrackDto(Guid TripId, int PointCount, IReadOnlyList<TrackPointDto> Points);

public sealed record GetTripTrackQuery(Guid TripId, Requester Requester) : IQuery<TripTrackDto>;

internal sealed class GetTripTrackQueryHandler(ITripRepository trips, ITrackPointRepository trackPoints)
    : IQueryHandler<GetTripTrackQuery, TripTrackDto>
{
    public async Task<Result<TripTrackDto>> HandleAsync(GetTripTrackQuery query, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadAsync(query.TripId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;

        // El historial completo del recorrido es información operativa: solo conductor asignado y su empresa.
        if (!query.Requester.CanOperateAsStaff(loaded.Value)) return ApplicationErrors.TripAccessDenied;

        var points = await trackPoints.GetByTripAsync(loaded.Value.Id, cancellationToken);
        var dto = points
            .OrderBy(p => p.RecordedAt)
            .Select(p => new TrackPointDto(p.Position.Latitude, p.Position.Longitude, p.RecordedAt, p.AccuracyMeters, p.SpeedKmh, p.HeadingDegrees))
            .ToList();

        return new TripTrackDto(loaded.Value.Id.Value, dto.Count, dto);
    }
}

// ───────────────────────── Posición rápida (respaldo del canal en vivo) ─────────────────────────

public sealed record TripPositionDto(
    Guid TripId, string Status, LivePositionDto? Position, Guid? NextStopId, double? NextStopDistanceMeters, int? NextStopEtaMinutes);

/// <summary>
/// Consulta liviana para el mapa del tutor cuando el canal en tiempo real no está disponible.
/// La posición sale de la caché (Redis) y, si no está, del agregado.
/// </summary>
public sealed record GetTripPositionQuery(Guid TripId, Requester Requester) : IQuery<TripPositionDto>;

internal sealed class GetTripPositionQueryHandler(
    ITripRepository trips, IGuardianDirectory guardians, ILastPositionCache cache, TimeProvider clock)
    : IQueryHandler<GetTripPositionQuery, TripPositionDto>
{
    public async Task<Result<TripPositionDto>> HandleAsync(GetTripPositionQuery query, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadAsync(query.TripId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;

        var allowed = query.Requester.Role == RequesterRole.Guardian
            ? GuardianAccessPolicy.CanViewTrip(trip,
                await guardians.GetStudentsOfGuardianAsync(new GuardianId(query.Requester.UserId), cancellationToken))
            : query.Requester.CanOperateAsStaff(trip);
        if (!allowed) return ApplicationErrors.TripAccessDenied;

        var now = clock.GetUtcNow();
        if (!GuardianAccessPolicy.CanSeeLivePosition(trip))
            return new TripPositionDto(trip.Id.Value, trip.Status.ToString(), null, null, null, null);

        var cached = await cache.GetAsync(trip.Id, cancellationToken);
        if (cached is not null && (trip.LastPositionAt is null || cached.RecordedAt >= trip.LastPositionAt))
        {
            var age = (int)Math.Max(0, (now - cached.RecordedAt).TotalSeconds);
            return new TripPositionDto(trip.Id.Value, trip.Status.ToString(),
                new LivePositionDto(cached.Latitude, cached.Longitude, cached.RecordedAt, cached.SpeedKmh, cached.HeadingDegrees, age),
                cached.NextStopId, cached.NextStopDistanceMeters, cached.NextStopEtaMinutes);
        }

        var next = trip.NextStop;
        double? distance = next is null || trip.LastPosition is null ? null : Math.Round(next.Position.DistanceMetersTo(trip.LastPosition));
        return new TripPositionDto(trip.Id.Value, trip.Status.ToString(),
            GetTripLiveStatusQueryHandler.PositionFor(trip, now),
            next?.StopId.Value, distance,
            distance is null ? null : EtaEstimator.EstimateMinutes(distance.Value, trip.LastSpeedKmh));
    }
}
