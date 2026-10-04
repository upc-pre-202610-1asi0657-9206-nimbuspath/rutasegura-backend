using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Abstractions;

// ───────────── Puertos de salida (driven ports) ─────────────
// La capa Application solo conoce estas interfaces. Las implementaciones (adaptadores) viven en
// Infrastructure: EF Core + MySQL para repositorios, ActiveMQ para el canal en tiempo real,
// réplicas locales alimentadas por eventos para rutas y vínculos tutor-alumno.

/// <summary>Persistencia del agregado Trip (adaptador: EF Core / MySQL, Etapa 3).</summary>
public interface ITripRepository
{
    Task<Trip?> GetByIdAsync(TripId id, CancellationToken cancellationToken);

    Task<Trip?> FindByRouteAndScheduledStartAsync(RouteId routeId, DateTimeOffset scheduledStart, CancellationToken cancellationToken);

    Task<IReadOnlyList<Trip>> ListByDriverAsync(DriverId driverId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    void Add(Trip trip);
}

/// <summary>Telemetría append-only del recorrido (adaptador: tabla track_points, Etapa 3).</summary>
public interface ITrackPointRepository
{
    Task AddRangeAsync(IReadOnlyCollection<TrackPoint> points, CancellationToken cancellationToken);

    Task<IReadOnlyList<TrackPoint>> GetByTripAsync(TripId tripId, CancellationToken cancellationToken);
}

/// <summary>
/// Canal de movimiento en tiempo real (adaptador: topic ActiveMQ "trip-tracking.live-positions", Etapa 4).
/// Es telemetría efímera: no pasa por el Outbox, si un mensaje se pierde el siguiente ping lo reemplaza.
/// </summary>
public interface ILivePositionPublisher
{
    Task PublishAsync(LivePositionUpdate update, CancellationToken cancellationToken);
}

/// <summary>Contrato del mensaje en tiempo real. Sin datos de alumnos (privacidad AC-01).</summary>
public sealed record LivePositionUpdate(
    Guid TripId,
    Guid RouteId,
    Guid VehicleId,
    string Status,
    double Latitude,
    double Longitude,
    DateTimeOffset RecordedAt,
    double? SpeedKmh,
    double? HeadingDegrees,
    Guid? NextStopId,
    double? NextStopDistanceMeters,
    int? NextStopEtaMinutes,
    int StudentsOnBoard);

/// <summary>Vínculos tutor → alumnos (réplica local de eventos de IAM/Administration, Etapa 4).</summary>
public interface IGuardianDirectory
{
    Task<IReadOnlyCollection<StudentId>> GetStudentsOfGuardianAsync(GuardianId guardianId, CancellationToken cancellationToken);
}

/// <summary>Rutas planificadas (réplica local de eventos de Administration, Etapa 4).</summary>
public interface IRouteCatalog
{
    Task<RouteSnapshot?> GetAsync(RouteId routeId, CancellationToken cancellationToken);
}

public sealed record RouteStopSnapshot(Guid StopId, int Sequence, string Name, double Latitude, double Longitude);

public sealed record RouteStudentSnapshot(Guid StudentId, Guid StopId);

public sealed record RouteSnapshot(
    Guid RouteId,
    string Name,
    TripDirection Direction,
    Guid DriverId,
    Guid VehicleId,
    TimeOnly DepartureTime,
    string TimeZoneId,
    IReadOnlyCollection<DayOfWeek> OperatingDays,
    IReadOnlyList<RouteStopSnapshot> Stops,
    IReadOnlyList<RouteStudentSnapshot> Students)
{
    public bool OperatesOn(DateOnly date) => OperatingDays.Contains(date.DayOfWeek);

    /// <summary>Hora de salida en la zona horaria de la ruta (Lima, UTC-5), como instante absoluto.</summary>
    public DateTimeOffset ScheduledStartOn(DateOnly date)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var local = date.ToDateTime(DepartureTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }
}
