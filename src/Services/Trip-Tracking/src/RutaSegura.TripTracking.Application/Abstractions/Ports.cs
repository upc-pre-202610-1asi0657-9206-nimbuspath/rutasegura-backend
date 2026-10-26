using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Abstractions;

// ───────────── Puertos de salida (driven ports) ─────────────
// La capa Application solo conoce estas interfaces. Las implementaciones (adaptadores) viven en
// Infrastructure: EF Core + MySQL para repositorios, RabbitMQ para el canal en tiempo real,
// réplicas locales alimentadas por eventos para rutas y vínculos tutor-alumno.

/// <summary>Persistencia del agregado Trip (adaptador: EF Core / MySQL, Etapa 3).</summary>
public interface ITripRepository
{
    Task<Trip?> GetByIdAsync(TripId id, CancellationToken cancellationToken);

    Task<Trip?> FindByRouteAndScheduledStartAsync(RouteId routeId, DateTimeOffset scheduledStart, CancellationToken cancellationToken);

    Task<IReadOnlyList<Trip>> ListByDriverAsync(DriverId driverId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>Viajes en los que viaja al menos uno de los alumnos indicados (pantalla de inicio del tutor).</summary>
    Task<IReadOnlyList<Trip>> ListByStudentsAsync(IReadOnlyCollection<StudentId> students, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>Monitoreo de la empresa: viajes de una organización en un rango, opcionalmente por estado.</summary>
    Task<IReadOnlyList<Trip>> ListByOrganizationAsync(OrganizationId organizationId, DateTimeOffset from, DateTimeOffset to, TripStatus? status, CancellationToken cancellationToken);

    /// <summary>Viajes de la ruta que siguen programados (aún no empezaron); se actualizan si la ruta cambia.</summary>
    Task<IReadOnlyList<Trip>> ListScheduledByRouteAsync(RouteId routeId, CancellationToken cancellationToken);

    void Add(Trip trip);
}

/// <summary>Telemetría append-only del recorrido (adaptador: tabla track_points, Etapa 3).</summary>
public interface ITrackPointRepository
{
    Task AddRangeAsync(IReadOnlyCollection<TrackPoint> points, CancellationToken cancellationToken);

    Task<IReadOnlyList<TrackPoint>> GetByTripAsync(TripId tripId, CancellationToken cancellationToken);
}

/// <summary>
/// Canal de movimiento en tiempo real (adaptador: canal RabbitMQ "trip-tracking.live-positions", Etapa 4).
/// Es telemetría efímera: no pasa por el Outbox, si un mensaje se pierde el siguiente ping lo reemplaza.
/// </summary>
public interface ILivePositionPublisher
{
    Task PublishAsync(LivePositionUpdate update, CancellationToken cancellationToken);
}

/// <summary>
/// Última posición aceptada de cada viaje activo (adaptador: Redis, clave <c>trip:{id}:position</c>, TS-15 / ADR-02.2).
/// Alimenta el canal en vivo y la consulta rápida de posición sin releer el historial relacional.
/// </summary>
public interface ILastPositionCache
{
    Task SetAsync(LivePositionUpdate update, CancellationToken cancellationToken);

    Task<LivePositionUpdate?> GetAsync(TripId tripId, CancellationToken cancellationToken);

    Task RemoveAsync(TripId tripId, CancellationToken cancellationToken);
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

/// <summary>Escritura de la réplica tutor → alumno. La usan los consumidores de eventos (Etapa 4).</summary>
public interface IGuardianDirectoryStore
{
    Task LinkAsync(GuardianId guardianId, StudentId studentId, CancellationToken cancellationToken);

    Task UnlinkAsync(GuardianId guardianId, StudentId studentId, CancellationToken cancellationToken);
}

/// <summary>Rutas planificadas (réplica local de eventos de Administration, Etapa 4).</summary>
public interface IRouteCatalog
{
    Task<RouteSnapshot?> GetAsync(RouteId routeId, CancellationToken cancellationToken);

    /// <summary>Todas las rutas vigentes (lo usa el programador diario de viajes).</summary>
    Task<IReadOnlyList<RouteSnapshot>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>Escritura de la réplica de rutas publicada por Administration (Route Planning).</summary>
public interface IRouteCatalogStore
{
    Task UpsertAsync(RouteSnapshot route, CancellationToken cancellationToken);

    Task RemoveAsync(RouteId routeId, CancellationToken cancellationToken);
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
    IReadOnlyList<RouteStudentSnapshot> Students,
    Guid OrganizationId = default)
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

/// <summary>
/// La unidad de trabajo no pudo confirmar porque otra operación cambió el mismo viaje a la vez
/// (concurrencia optimista) o violó una restricción única (ej. viaje ya programado).
/// La API responde 409 con <see cref="Code"/>; el cliente reintenta con el mismo clientEventId sin duplicar.
/// </summary>
public sealed class PersistenceConflictException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public const string ConcurrencyCode = "CONCURRENCY_CONFLICT";
    public const string DuplicateCode = "DUPLICATE_RECORD";

    public string Code { get; } = code;
}
