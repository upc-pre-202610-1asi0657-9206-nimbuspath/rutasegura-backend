namespace RutaSegura.Notification.Application.Contracts.V1;

/// <summary>Metadatos neutrales. Infrastructure los obtiene del sobre IntegrationMessage de BuildingBlocks.Messaging.</summary>
public sealed record NotificationEventMetadata(Guid MessageId, string MessageType, int SchemaVersion,
    DateTimeOffset OccurredAt, string CorrelationId);

// Contratos de entrada de Notification: no referencian agregados/assemblies de TripTracking.
// OrganizationId lo agrega el productor a partir del propietario real del viaje.
public abstract record TripNotificationEventV1(Guid OrganizationId, Guid TripId);
public sealed record StudentBoardedV1(Guid OrganizationId, Guid TripId, Guid StudentId, Guid StopId)
    : TripNotificationEventV1(OrganizationId, TripId);
public sealed record StudentAlightedV1(Guid OrganizationId, Guid TripId, Guid StudentId, Guid StopId)
    : TripNotificationEventV1(OrganizationId, TripId);
public sealed record VehicleApproachingStopV1(Guid OrganizationId, Guid TripId, Guid StopId,
    IReadOnlyList<Guid> StudentIds, int EtaMinutes) : TripNotificationEventV1(OrganizationId, TripId);
public sealed record IncidentReportedV1(Guid OrganizationId, Guid TripId, Guid IncidentId,
    bool NotifyGuardians, IReadOnlyList<Guid> AffectedStudentIds) : TripNotificationEventV1(OrganizationId, TripId);
public sealed record SosRaisedV1(Guid OrganizationId, Guid TripId, Guid DriverId, Guid VehicleId,
    IReadOnlyList<Guid> AffectedStudentIds) : TripNotificationEventV1(OrganizationId, TripId);
public enum TripDirectionV1 { Unknown, Pickup, Return }
// El resumen de cierre de un retorno NO confirma otra entrega: ya fue comunicada al desembarcar.
// SchoolDeliveredStudentIds contiene solo las entregas nuevas confirmadas por el cierre del recojo.
public sealed record TripCompletedV1(Guid OrganizationId, Guid TripId, Guid RouteId,
    TripDirectionV1 Direction, IReadOnlyList<Guid> SchoolDeliveredStudentIds) : TripNotificationEventV1(OrganizationId, TripId);

public static class TripNotificationContracts
{
    public const string Exchange = "trip-tracking.events";
    public const string ConsumerQueue = "notification.trip-events.v1";

    // MessageType/routing keys propuestos: deben publicarse iguales desde el Outbox del productor.
    public static string MessageType(TripNotificationEventV1 message) => message switch
    {
        StudentBoardedV1 => "trip.student-boarded.v1",
        StudentAlightedV1 => "trip.student-alighted.v1",
        VehicleApproachingStopV1 => "trip.vehicle-approaching-stop.v1",
        IncidentReportedV1 => "trip.incident-reported.v1",
        SosRaisedV1 => "trip.sos-raised.v1",
        TripCompletedV1 => "trip.completed.v1",
        _ => throw new ArgumentException("Contrato de evento no soportado.", nameof(message))
    };
}
