using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips.Events;

namespace RutaSegura.TripTracking.Domain.Trips;

/// <summary>
/// Agregado raíz del bounded context Trip &amp; Tracking.
/// Fusiona Trip Execution + Tracking (It. 1, ADR): una posición GPS solo tiene sentido
/// dentro de un viaje activo (C-12), por eso el viaje es quien valida cada lectura.
///
/// Todas las acciones del conductor llevan un <see cref="ClientEventId"/>: si llega repetido
/// (reintento o sincronización offline) la acción es un no-op y devuelve <c>false</c>.
/// </summary>
public sealed class Trip : AggregateRoot<TripId>
{
    /// <summary>El conductor puede iniciar hasta 60 min antes de la hora programada.</summary>
    public static readonly TimeSpan StartWindowBeforeSchedule = TimeSpan.FromMinutes(60);

    /// <summary>Iniciar más de 10 min tarde deja el viaje en estado Delayed.</summary>
    public static readonly TimeSpan LateStartTolerance = TimeSpan.FromMinutes(10);

    private readonly List<TripStop> _stops = [];
    private readonly List<TripPassenger> _passengers = [];
    private readonly List<Incident> _incidents = [];
    private readonly List<SosAlert> _sosAlerts = [];
    private readonly HashSet<ClientEventId> _processedClientEvents = [];

    public RouteId RouteId { get; private set; }
    public string RouteName { get; private set; } = string.Empty;
    public TripDirection Direction { get; private set; }
    public DriverId DriverId { get; private set; }
    public VehicleId VehicleId { get; private set; }
    public DateTimeOffset ScheduledStart { get; private set; }
    public TripStatus Status { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    // Última posición aceptada (lo que ve el tutor en el mapa).
    public GeoPosition? LastPosition { get; private set; }
    public DateTimeOffset? LastPositionAt { get; private set; }
    public double? LastSpeedKmh { get; private set; }
    public double? LastHeadingDegrees { get; private set; }

    public IReadOnlyList<TripStop> Stops => _stops.OrderBy(s => s.Sequence).ToList();
    public IReadOnlyList<TripPassenger> Passengers => _passengers.AsReadOnly();
    public IReadOnlyList<Incident> Incidents => _incidents.AsReadOnly();
    public IReadOnlyList<SosAlert> SosAlerts => _sosAlerts.AsReadOnly();
    public IReadOnlyCollection<ClientEventId> ProcessedClientEvents => _processedClientEvents;

    public bool IsActive => Status is TripStatus.InProgress or TripStatus.Delayed;
    public SosAlert? ActiveSos => _sosAlerts.FirstOrDefault(s => s.IsActive);
    public TripStop? NextStop => _stops.Where(s => !s.IsReached).MinBy(s => s.Sequence);
    public IEnumerable<TripPassenger> PassengersOnBoard => _passengers.Where(p => p.Status == PassengerStatus.Boarded);

    private Trip() { } // EF Core

    // ───────────────────────────── Programación ─────────────────────────────

    public static Trip Schedule(
        TripId id,
        RouteId routeId,
        string routeName,
        TripDirection direction,
        DriverId driverId,
        VehicleId vehicleId,
        DateTimeOffset scheduledStart,
        IReadOnlyCollection<TripStopDefinition> stops,
        IReadOnlyCollection<TripPassengerDefinition> passengers,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(routeName))
            throw Invalid("El viaje debe tener el nombre de la ruta.");
        if (driverId.Value == Guid.Empty || vehicleId.Value == Guid.Empty)
            throw Invalid("El viaje necesita un conductor y un vehículo asignados.");
        if (stops.Count == 0)
            throw Invalid("La ruta debe tener al menos una parada.");
        if (stops.Select(s => s.StopId).Distinct().Count() != stops.Count ||
            stops.Select(s => s.Sequence).Distinct().Count() != stops.Count)
            throw Invalid("Las paradas no pueden repetir identificador ni orden.");
        if (passengers.Select(p => p.StudentId).Distinct().Count() != passengers.Count)
            throw Invalid("Un alumno no puede figurar dos veces en el mismo viaje.");

        var stopIds = stops.Select(s => s.StopId).ToHashSet();
        if (passengers.Any(p => !stopIds.Contains(p.StopId)))
            throw Invalid("Todos los alumnos deben estar asignados a una parada de la ruta.");

        var trip = new Trip
        {
            Id = id,
            RouteId = routeId,
            RouteName = routeName.Trim(),
            Direction = direction,
            DriverId = driverId,
            VehicleId = vehicleId,
            ScheduledStart = scheduledStart,
            Status = TripStatus.Scheduled
        };
        trip._stops.AddRange(stops.Select(s => new TripStop(s)));
        trip._passengers.AddRange(passengers.Select(p => new TripPassenger(p)));

        trip.Raise(new TripScheduled(id, routeId, driverId, vehicleId, direction, scheduledStart, now));
        return trip;

        static DomainException Invalid(string message) => new(TripErrors.InvalidDefinition, message);
    }

    // ───────────────────────────── Ciclo de vida ─────────────────────────────

    public bool Start(DriverId driverId, ClientEventId clientEventId, DateTimeOffset now)
    {
        if (IsDuplicate(clientEventId)) return false;
        EnsureOperatedBy(driverId);

        if (Status != TripStatus.Scheduled)
            throw new DomainException(TripErrors.InvalidTransition, $"No se puede iniciar un viaje en estado {Status}.");

        if (now < ScheduledStart - StartWindowBeforeSchedule)
            throw new DomainException(TripErrors.StartTooEarly,
                $"El viaje solo puede iniciarse desde {StartWindowBeforeSchedule.TotalMinutes:0} min antes de la hora programada.");

        Status = TripStatus.InProgress;
        StartedAt = now;

        var expected = _passengers.Where(p => p.Status != PassengerStatus.Absent).Select(p => p.StudentId).ToList();
        Raise(new TripStarted(Id, RouteId, DriverId, VehicleId, Direction, expected, now));

        var lateBy = now - ScheduledStart;
        if (lateBy > LateStartTolerance)
            MarkDelayed($"Inicio con {lateBy.TotalMinutes:0} min de retraso", now);

        MarkProcessed(clientEventId);
        return true;
    }

    public bool Complete(DriverId driverId, ClientEventId clientEventId, DateTimeOffset now)
    {
        if (IsDuplicate(clientEventId)) return false;
        EnsureOperatedBy(driverId);

        if (!IsActive)
            throw new DomainException(TripErrors.InvalidTransition, $"No se puede finalizar un viaje en estado {Status}.");

        if (ActiveSos is not null)
            throw new DomainException(TripErrors.HasActiveSos, "Hay una alerta SOS activa: la empresa debe cerrarla antes de finalizar.");

        var pending = _passengers.Count(p => p.Status == PassengerStatus.Pending);
        if (pending > 0)
            throw new DomainException(TripErrors.HasPendingStudents, $"Faltan registrar {pending} alumno(s).");

        if (Direction == TripDirection.Pickup)
        {
            // Recojo: al llegar al colegio todos los que van a bordo quedan entregados.
            foreach (var passenger in PassengersOnBoard.ToList())
                passenger.Alight(now, LastPosition);
        }
        else if (PassengersOnBoard.Any())
        {
            throw new DomainException(TripErrors.HasStudentsOnBoard,
                $"Aún hay {PassengersOnBoard.Count()} alumno(s) a bordo sin registrar su bajada.");
        }

        Status = TripStatus.Completed;
        CompletedAt = now;

        Raise(new TripCompleted(
            Id,
            RouteId,
            _passengers.Where(p => p.Status == PassengerStatus.Alighted).Select(p => p.StudentId).ToList(),
            _passengers.Where(p => p.Status == PassengerStatus.Absent).Select(p => p.StudentId).ToList(),
            _incidents.Count,
            StartedAt!.Value,
            now));

        MarkProcessed(clientEventId);
        return true;
    }

    /// <summary>Cancelación administrativa: solo antes de iniciar.</summary>
    public void Cancel(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(TripErrors.CancellationReasonRequired, "Indica el motivo de la cancelación.");

        if (Status != TripStatus.Scheduled)
            throw new DomainException(TripErrors.InvalidTransition, $"No se puede cancelar un viaje en estado {Status}.");

        Status = TripStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = reason.Trim();
        Raise(new TripCancelled(Id, CancellationReason, now));
    }

    // ───────────────────────────── Tracking GPS ─────────────────────────────

    /// <summary>
    /// Valida y registra una lectura GPS. Las lecturas "malas" no lanzan excepción: se ignoran
    /// con un motivo, porque los lotes offline traen ruido normal y no deben fallar completos.
    /// </summary>
    public PositionRecordingResult RecordPosition(DriverId driverId, GpsReading reading, DateTimeOffset receivedAt)
    {
        EnsureOperatedBy(driverId);

        if (!IsActive)
            throw new DomainException(TrackingErrors.RequiresActiveTrip,
                "La ubicación solo se comparte mientras un viaje está en curso.");

        if (reading.RecordedAt > receivedAt + TrackingPolicy.MaxClockSkew)
            return PositionRecordingResult.Ignored(PositionRecordingOutcome.IgnoredFutureTimestamp);

        if (reading.RecordedAt < StartedAt!.Value - TrackingPolicy.MaxClockSkew)
            return PositionRecordingResult.Ignored(PositionRecordingOutcome.IgnoredBeforeTripStart);

        if (LastPositionAt is { } lastAt && reading.RecordedAt <= lastAt)
            return PositionRecordingResult.Ignored(PositionRecordingOutcome.IgnoredStaleOrDuplicate);

        if (reading.AccuracyMeters > TrackingPolicy.MaxAccuracyMeters)
            return PositionRecordingResult.Ignored(PositionRecordingOutcome.IgnoredLowAccuracy);

        if (IsOutlier(reading))
            return PositionRecordingResult.Ignored(PositionRecordingOutcome.IgnoredOutlier);

        LastPosition = reading.Position;
        LastPositionAt = reading.RecordedAt;
        LastSpeedKmh = reading.SpeedKmh;
        LastHeadingDegrees = reading.HeadingDegrees;

        DetectStopProgress(reading);

        var point = new TrackPoint(Id, reading.Position, reading.RecordedAt, receivedAt,
            reading.AccuracyMeters, reading.SpeedKmh, reading.HeadingDegrees);
        return new PositionRecordingResult(PositionRecordingOutcome.Accepted, point);
    }

    private bool IsOutlier(GpsReading reading)
    {
        if (LastPosition is null || LastPositionAt is null) return false;

        var distance = LastPosition.DistanceMetersTo(reading.Position);
        if (distance < 100) return false; // el "jitter" de pocos metros no es un salto

        var seconds = (reading.RecordedAt - LastPositionAt.Value).TotalSeconds;
        var impliedKmh = distance / seconds * 3.6;
        return impliedKmh > TrackingPolicy.MaxPlausibleSpeedKmh;
    }

    private void DetectStopProgress(GpsReading reading)
    {
        foreach (var stop in _stops.Where(s => !s.IsReached).OrderBy(s => s.Sequence))
        {
            if (stop.Position.DistanceMetersTo(reading.Position) <= TrackingPolicy.StopArrivalRadiusMeters)
            {
                stop.MarkReached(reading.RecordedAt);
                Raise(new StopReached(Id, stop.StopId, stop.Sequence, reading.RecordedAt));
            }
        }

        var next = NextStop;
        if (next is null || next.ApproachNotified) return;

        var distance = next.Position.DistanceMetersTo(reading.Position);
        if (distance > TrackingPolicy.StopApproachRadiusMeters) return;

        next.MarkApproachNotified();
        Raise(new VehicleApproachingStop(
            Id, next.StopId, next.Name, Math.Round(distance), EtaEstimator.EstimateMinutes(distance, reading.SpeedKmh),
            StudentsExpectedAt(next.StopId), reading.RecordedAt));
    }

    /// <summary>Recojo: los que esperan en la parada. Retorno: los que bajan en esa parada.</summary>
    public IReadOnlyList<StudentId> StudentsExpectedAt(StopId stopId) =>
        _passengers
            .Where(p => p.StopId == stopId)
            .Where(p => Direction == TripDirection.Pickup
                ? p.Status == PassengerStatus.Pending
                : p.Status == PassengerStatus.Boarded)
            .Select(p => p.StudentId)
            .ToList();

    // ───────────────────────────── Pasajeros ─────────────────────────────

    public bool RecordBoarding(DriverId driverId, StudentId studentId, ClientEventId clientEventId,
        DateTimeOffset? occurredAt, GeoPosition? position, DateTimeOffset now)
    {
        if (IsDuplicate(clientEventId)) return false;
        EnsureOperatedBy(driverId);
        EnsureActive();

        var at = ResolveTimestamp(occurredAt, now);
        var passenger = GetPassenger(studentId);

        if (passenger.Status == PassengerStatus.Boarded)
        {
            MarkProcessed(clientEventId); // idempotente por estado
            return false;
        }

        var where = position ?? LastPosition;
        passenger.Board(at, where);
        Raise(new StudentBoarded(Id, studentId, passenger.StopId, where, at));

        MarkProcessed(clientEventId);
        return true;
    }

    public bool RecordAlighting(DriverId driverId, StudentId studentId, ClientEventId clientEventId,
        DateTimeOffset? occurredAt, GeoPosition? position, DateTimeOffset now)
    {
        if (IsDuplicate(clientEventId)) return false;
        EnsureOperatedBy(driverId);
        EnsureActive();

        var at = ResolveTimestamp(occurredAt, now);
        var passenger = GetPassenger(studentId);

        if (passenger.Status == PassengerStatus.Alighted)
        {
            MarkProcessed(clientEventId);
            return false;
        }

        var where = position ?? LastPosition;
        passenger.Alight(at, where);
        Raise(new StudentAlighted(Id, studentId, passenger.StopId, where, at));

        MarkProcessed(clientEventId);
        return true;
    }

    /// <summary>Botón "No vino" del conductor.</summary>
    public bool MarkStudentAbsent(DriverId driverId, StudentId studentId, ClientEventId clientEventId,
        DateTimeOffset? occurredAt, DateTimeOffset now)
    {
        if (IsDuplicate(clientEventId)) return false;
        EnsureOperatedBy(driverId);
        EnsureActive();

        var at = ResolveTimestamp(occurredAt, now);
        var passenger = GetPassenger(studentId);

        if (passenger.Status == PassengerStatus.Absent)
        {
            MarkProcessed(clientEventId);
            return false;
        }

        passenger.MarkAbsent(AbsenceSource.Driver, note: null);
        Raise(new StudentMarkedAbsent(Id, studentId, AbsenceSource.Driver, null, at));

        MarkProcessed(clientEventId);
        return true;
    }

    /// <summary>El apoderado avisa que su hijo no asiste (antes o durante el viaje).</summary>
    public bool NotifyGuardianAbsence(StudentId studentId, string? note, DateTimeOffset now)
    {
        if (Status is TripStatus.Completed or TripStatus.Cancelled)
            throw new DomainException(TripErrors.InvalidTransition, $"El viaje ya está {Status}.");

        var passenger = GetPassenger(studentId);
        if (passenger.Status == PassengerStatus.Absent) return false;

        passenger.MarkAbsent(AbsenceSource.Guardian, note);
        Raise(new StudentMarkedAbsent(Id, studentId, AbsenceSource.Guardian, passenger.AbsenceNote, now));
        return true;
    }

    /// <summary>Botón "Deshacer" del conductor.</summary>
    public bool UndoPassengerRecord(DriverId driverId, StudentId studentId, ClientEventId clientEventId, DateTimeOffset now)
    {
        if (IsDuplicate(clientEventId)) return false;
        EnsureOperatedBy(driverId);
        EnsureActive();

        var previous = GetPassenger(studentId).Undo();
        Raise(new PassengerRecordUndone(Id, studentId, previous, now));

        MarkProcessed(clientEventId);
        return true;
    }

    // ───────────────────────────── Incidentes y SOS ─────────────────────────────

    public bool ReportIncident(DriverId driverId, IncidentType type, string? detail, bool notifyGuardians,
        ClientEventId clientEventId, DateTimeOffset? occurredAt, GeoPosition? position, DateTimeOffset now)
    {
        if (IsDuplicate(clientEventId)) return false;
        EnsureOperatedBy(driverId);
        EnsureActive();

        var at = ResolveTimestamp(occurredAt, now);
        var cleanDetail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();

        if (type == IncidentType.Other && cleanDetail is null)
            throw new DomainException(TripErrors.IncidentDetailRequired, "Describe el incidente cuando eliges \"Otro\".");
        if (cleanDetail?.Length > Incident.MaxDetailLength)
            throw new DomainException(TripErrors.IncidentDetailTooLong, $"El detalle admite hasta {Incident.MaxDetailLength} caracteres.");

        var where = position ?? LastPosition;
        var incident = new Incident(IncidentId.New(), type, cleanDetail, notifyGuardians, at, where);
        _incidents.Add(incident);

        var affected = _passengers
            .Where(p => p.Status is PassengerStatus.Boarded or PassengerStatus.Pending)
            .Select(p => p.StudentId)
            .ToList();
        Raise(new IncidentReported(Id, incident.Id, type, cleanDetail, notifyGuardians, where, affected, at));

        if (Status == TripStatus.InProgress && Incident.CausesDelay(type))
            MarkDelayed($"Incidente: {type}", at);

        MarkProcessed(clientEventId);
        return true;
    }

    /// <summary>
    /// Emergencia. Por seguridad NUNCA falla por falta de GPS: si no hay posición se envía sin ella.
    /// La cuenta regresiva de 5 s para cancelar ocurre en el celular, aquí solo llega el SOS confirmado.
    /// </summary>
    public bool RaiseSos(DriverId driverId, ClientEventId clientEventId, DateTimeOffset? occurredAt,
        GeoPosition? position, DateTimeOffset now)
    {
        if (IsDuplicate(clientEventId)) return false;
        EnsureOperatedBy(driverId);
        EnsureActive();

        if (ActiveSos is not null)
        {
            MarkProcessed(clientEventId);
            return false;
        }

        var at = ResolveTimestamp(occurredAt, now);
        var where = position ?? LastPosition;
        _sosAlerts.Add(new SosAlert(at, where));

        Raise(new SosRaised(Id, DriverId, VehicleId, where,
            PassengersOnBoard.Select(p => p.StudentId).ToList(), at));

        MarkProcessed(clientEventId);
        return true;
    }

    /// <summary>La empresa de transporte cierra la emergencia.</summary>
    public void ResolveSos(Guid resolvedBy, DateTimeOffset now)
    {
        var sos = ActiveSos ?? throw new DomainException(TripErrors.NoActiveSos, "El viaje no tiene una alerta SOS activa.");
        sos.Resolve(resolvedBy, now);
        Raise(new SosResolved(Id, resolvedBy, now));
    }

    // ───────────────────────────── Reglas auxiliares ─────────────────────────────

    public bool IsOperatedBy(DriverId driverId) => DriverId == driverId;

    public bool HasPassenger(StudentId studentId) => _passengers.Any(p => p.StudentId == studentId);

    private void MarkDelayed(string reason, DateTimeOffset at)
    {
        if (Status == TripStatus.Delayed) return;
        Status = TripStatus.Delayed;
        Raise(new TripDelayed(Id, reason, at));
    }

    private void EnsureOperatedBy(DriverId driverId)
    {
        if (!IsOperatedBy(driverId))
            throw new DomainException(TripErrors.DriverMismatch, "Este viaje está asignado a otro conductor.");
    }

    private void EnsureActive()
    {
        if (!IsActive)
            throw new DomainException(TripErrors.NotActive, $"El viaje no está en curso (estado {Status}).");
    }

    private TripPassenger GetPassenger(StudentId studentId) =>
        _passengers.FirstOrDefault(p => p.StudentId == studentId)
        ?? throw new DomainException(TripErrors.StudentNotInRoster, $"El alumno {studentId} no pertenece a este viaje.");

    private static DateTimeOffset ResolveTimestamp(DateTimeOffset? occurredAt, DateTimeOffset now)
    {
        var at = occurredAt ?? now;
        if (at > now + TrackingPolicy.MaxClockSkew)
            throw new DomainException(TripErrors.EventTimestampInvalid, "La hora del evento está en el futuro: revisa el reloj del celular.");
        return at;
    }

    private bool IsDuplicate(ClientEventId clientEventId) => _processedClientEvents.Contains(clientEventId);

    private void MarkProcessed(ClientEventId clientEventId) => _processedClientEvents.Add(clientEventId);
}
