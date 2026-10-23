using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Domain.Trips.Events;

namespace RutaSegura.TripTracking.Application.IntegrationEvents;

// ─────────────────────────────────────────────────────────────────────────────
// Contratos PUBLICADOS por Trip & Tracking en RabbitMQ (exchange topic "trip-tracking.events").
// Son la "Published Language" del contexto: tipos primitivos, versionados y sin clases del dominio,
// para que Notification y Administration los consuman sin referenciar este servicio.
//
//   routing key = MessageType (ej. "trip-tracking.student-boarded")
//   headers     = message-id, message-type, schema-version, occurred-at, correlation-id
//   body        = JSON camelCase del payload
//
// Regla de evolución: agregar campos opcionales es compatible; renombrar o quitar exige SchemaVersion + 1.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record GeoPointV1(double Latitude, double Longitude)
{
    internal static GeoPointV1? From(GeoPosition? position) => position is null ? null : new(position.Latitude, position.Longitude);
}

public sealed record TripScheduledV1(
    Guid TripId, Guid RouteId, Guid? OrganizationId, string RouteName, string Direction,
    Guid DriverId, Guid VehicleId, DateTimeOffset ScheduledStart);

public sealed record TripStartedV1(
    Guid TripId, Guid RouteId, Guid? OrganizationId, string RouteName, string Direction,
    Guid DriverId, Guid VehicleId, IReadOnlyList<Guid> ExpectedStudents, DateTimeOffset StartedAt);

public sealed record TripDelayedV1(Guid TripId, Guid RouteId, Guid? OrganizationId, string RouteName, string Reason,
    IReadOnlyList<Guid> AffectedStudents, DateTimeOffset OccurredAt);

public sealed record StudentBoardedV1(Guid TripId, Guid RouteId, Guid? OrganizationId, string RouteName,
    Guid StudentId, Guid StopId, GeoPointV1? Position, DateTimeOffset OccurredAt);

public sealed record StudentAlightedV1(Guid TripId, Guid RouteId, Guid? OrganizationId, string RouteName,
    Guid StudentId, Guid StopId, GeoPointV1? Position, DateTimeOffset OccurredAt);

public sealed record StudentMarkedAbsentV1(Guid TripId, Guid RouteId, Guid? OrganizationId, string RouteName,
    Guid StudentId, string Source, string? Note, DateTimeOffset OccurredAt);

public sealed record PassengerRecordUndoneV1(Guid TripId, Guid RouteId, Guid? OrganizationId,
    Guid StudentId, string PreviousStatus, DateTimeOffset OccurredAt);

public sealed record VehicleApproachingStopV1(Guid TripId, Guid RouteId, Guid? OrganizationId, string RouteName,
    Guid StopId, string StopName, double DistanceMeters, int EtaMinutes, IReadOnlyList<Guid> StudentsAtStop, DateTimeOffset OccurredAt);

public sealed record StopReachedV1(Guid TripId, Guid RouteId, Guid? OrganizationId, Guid StopId, int Sequence, DateTimeOffset OccurredAt);

public sealed record IncidentReportedV1(Guid TripId, Guid RouteId, Guid? OrganizationId, string RouteName,
    Guid IncidentId, string Type, string? Detail, bool NotifyGuardians, GeoPointV1? Position,
    IReadOnlyList<Guid> AffectedStudents, DateTimeOffset OccurredAt);

public sealed record SosRaisedV1(Guid TripId, Guid RouteId, Guid? OrganizationId, string RouteName,
    Guid DriverId, Guid VehicleId, GeoPointV1? Position, IReadOnlyList<Guid> StudentsOnBoard, DateTimeOffset OccurredAt);

public sealed record SosResolvedV1(Guid TripId, Guid RouteId, Guid? OrganizationId, Guid ResolvedBy, DateTimeOffset OccurredAt);

public sealed record TripCompletedV1(Guid TripId, Guid RouteId, Guid? OrganizationId, string RouteName,
    IReadOnlyList<Guid> DeliveredStudents, IReadOnlyList<Guid> AbsentStudents, int IncidentCount,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt);

public sealed record TripCancelledV1(Guid TripId, Guid RouteId, Guid? OrganizationId, string Reason, DateTimeOffset OccurredAt);

/// <summary>Evento de dominio ya traducido al contrato publicado, listo para guardarse en el Outbox.</summary>
public sealed record OutgoingIntegrationEvent(Guid EventId, string MessageType, int SchemaVersion, DateTimeOffset OccurredAt, object Payload);

/// <summary>
/// Traductor domain event → integration event (Anti-Corruption hacia afuera). Recibe el agregado para
/// enriquecer el mensaje (empresa, ruta) sin que el dominio tenga que cargar esos datos en cada evento.
/// </summary>
public static class TripIntegrationEventMapper
{
    public const string Prefix = "trip-tracking.";

    public static class Types
    {
        public const string TripScheduled = Prefix + "trip-scheduled";
        public const string TripStarted = Prefix + "trip-started";
        public const string TripDelayed = Prefix + "trip-delayed";
        public const string StudentBoarded = Prefix + "student-boarded";
        public const string StudentAlighted = Prefix + "student-alighted";
        public const string StudentMarkedAbsent = Prefix + "student-marked-absent";
        public const string PassengerRecordUndone = Prefix + "passenger-record-undone";
        public const string VehicleApproachingStop = Prefix + "vehicle-approaching-stop";
        public const string StopReached = Prefix + "stop-reached";
        public const string IncidentReported = Prefix + "incident-reported";
        public const string SosRaised = Prefix + "sos-raised";
        public const string SosResolved = Prefix + "sos-resolved";
        public const string TripCompleted = Prefix + "trip-completed";
        public const string TripCancelled = Prefix + "trip-cancelled";
    }

    public static OutgoingIntegrationEvent Map(Trip trip, IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(domainEvent);

        var tripId = trip.Id.Value;
        var routeId = trip.RouteId.Value;
        Guid? org = trip.OrganizationId.IsEmpty ? null : trip.OrganizationId.Value;
        var route = trip.RouteName;

        (string Type, object Payload) mapped = domainEvent switch
        {
            TripScheduled e => ((string, object))(Types.TripScheduled, new TripScheduledV1(tripId, routeId, org, route, e.Direction.ToString(),
                e.DriverId.Value, e.VehicleId.Value, e.ScheduledStart)),
            TripStarted e => (Types.TripStarted, new TripStartedV1(tripId, routeId, org, route, e.Direction.ToString(),
                e.DriverId.Value, e.VehicleId.Value, Ids(e.ExpectedStudents), e.OccurredOn)),
            TripDelayed e => (Types.TripDelayed, new TripDelayedV1(tripId, routeId, org, route, e.Reason,
                Ids(trip.Passengers.Where(p => p.Status is PassengerStatus.Pending or PassengerStatus.Boarded).Select(p => p.StudentId)),
                e.OccurredOn)),
            StudentBoarded e => (Types.StudentBoarded, new StudentBoardedV1(tripId, routeId, org, route,
                e.StudentId.Value, e.StopId.Value, GeoPointV1.From(e.Position), e.OccurredOn)),
            StudentAlighted e => (Types.StudentAlighted, new StudentAlightedV1(tripId, routeId, org, route,
                e.StudentId.Value, e.StopId.Value, GeoPointV1.From(e.Position), e.OccurredOn)),
            StudentMarkedAbsent e => (Types.StudentMarkedAbsent, new StudentMarkedAbsentV1(tripId, routeId, org, route,
                e.StudentId.Value, e.Source.ToString(), e.Note, e.OccurredOn)),
            PassengerRecordUndone e => (Types.PassengerRecordUndone, new PassengerRecordUndoneV1(tripId, routeId, org,
                e.StudentId.Value, e.PreviousStatus.ToString(), e.OccurredOn)),
            VehicleApproachingStop e => (Types.VehicleApproachingStop, new VehicleApproachingStopV1(tripId, routeId, org, route,
                e.StopId.Value, e.StopName, e.DistanceMeters, e.EtaMinutes, Ids(e.StudentsAtStop), e.OccurredOn)),
            StopReached e => (Types.StopReached, new StopReachedV1(tripId, routeId, org, e.StopId.Value, e.Sequence, e.OccurredOn)),
            IncidentReported e => (Types.IncidentReported, new IncidentReportedV1(tripId, routeId, org, route,
                e.IncidentId.Value, e.Type.ToString(), e.Detail, e.NotifyGuardians, GeoPointV1.From(e.Position),
                Ids(e.AffectedStudents), e.OccurredOn)),
            SosRaised e => (Types.SosRaised, new SosRaisedV1(tripId, routeId, org, route,
                e.DriverId.Value, e.VehicleId.Value, GeoPointV1.From(e.Position), Ids(e.StudentsOnBoard), e.OccurredOn)),
            SosResolved e => (Types.SosResolved, new SosResolvedV1(tripId, routeId, org, e.ResolvedBy, e.OccurredOn)),
            TripCompleted e => (Types.TripCompleted, new TripCompletedV1(tripId, routeId, org, route,
                Ids(e.DeliveredStudents), Ids(e.AbsentStudents), e.IncidentCount, e.StartedAt, e.OccurredOn)),
            TripCancelled e => (Types.TripCancelled, new TripCancelledV1(tripId, routeId, org, e.Reason, e.OccurredOn)),
            _ => throw new InvalidOperationException($"No hay contrato de integración para {domainEvent.GetType().Name}.")
        };

        return new OutgoingIntegrationEvent(domainEvent.EventId, mapped.Type, 1, domainEvent.OccurredOn, mapped.Payload);
    }

    private static IReadOnlyList<Guid> Ids(IEnumerable<StudentId> students) => students.Select(s => s.Value).ToList();
}
