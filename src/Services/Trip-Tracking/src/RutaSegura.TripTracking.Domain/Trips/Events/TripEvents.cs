using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;

namespace RutaSegura.TripTracking.Domain.Trips.Events;

// Domain events del agregado Trip. En la Etapa 4 se mapean 1:1 a eventos de integración
// publicados en RabbitMQ (topic "trip-tracking.events") a través del Outbox.
// Consumidores principales: Notification Service (push a tutores/empresa) y Administration (reportes).

public sealed record TripScheduled(
    TripId TripId, RouteId RouteId, DriverId DriverId, VehicleId VehicleId,
    TripDirection Direction, DateTimeOffset ScheduledStart, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);

public sealed record TripStarted(
    TripId TripId, RouteId RouteId, DriverId DriverId, VehicleId VehicleId, TripDirection Direction,
    IReadOnlyList<StudentId> ExpectedStudents, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);

public sealed record TripDelayed(TripId TripId, string Reason, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);

public sealed record StudentBoarded(
    TripId TripId, StudentId StudentId, StopId StopId, GeoPosition? Position, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);

public sealed record StudentAlighted(
    TripId TripId, StudentId StudentId, StopId StopId, GeoPosition? Position, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);

public sealed record StudentMarkedAbsent(
    TripId TripId, StudentId StudentId, AbsenceSource Source, string? Note, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);

public sealed record PassengerRecordUndone(
    TripId TripId, StudentId StudentId, PassengerStatus PreviousStatus, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);

public sealed record VehicleApproachingStop(
    TripId TripId, StopId StopId, string StopName, double DistanceMeters, int EtaMinutes,
    IReadOnlyList<StudentId> StudentsAtStop, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);

public sealed record StopReached(
    TripId TripId, StopId StopId, int Sequence, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);

public sealed record IncidentReported(
    TripId TripId, IncidentId IncidentId, IncidentType Type, string? Detail, bool NotifyGuardians,
    GeoPosition? Position, IReadOnlyList<StudentId> AffectedStudents, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);

public sealed record SosRaised(
    TripId TripId, DriverId DriverId, VehicleId VehicleId, GeoPosition? Position,
    IReadOnlyList<StudentId> StudentsOnBoard, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);

public sealed record SosResolved(TripId TripId, Guid ResolvedBy, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);

public sealed record TripCompleted(
    TripId TripId, RouteId RouteId, IReadOnlyList<StudentId> DeliveredStudents, IReadOnlyList<StudentId> AbsentStudents,
    int IncidentCount, DateTimeOffset StartedAt, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);

public sealed record TripCancelled(TripId TripId, string Reason, DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);
