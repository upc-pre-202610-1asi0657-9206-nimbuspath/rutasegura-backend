namespace Monolith.Administration.Domain.Model.Commands;

// Commands produced from the integration events Trip & Tracking publishes, plus the administrator's
// follow-up of the resulting alerts. OrganizationId is optional in the events (demo trips may not carry it);
// in that case the organization of the route is used.

/// <summary>From <c>trip-tracking.incident-reported</c> (IncidentReportedEvent).</summary>
public record RecordIncidentReportedCommand(
    Guid TripId,
    Guid RouteId,
    Guid? OrganizationId,
    string? RouteName,
    Guid IncidentId,
    string IncidentType,
    string? Detail,
    double? Latitude,
    double? Longitude,
    int AffectedStudents,
    DateTimeOffset OccurredAt);

/// <summary>From <c>trip-tracking.sos-raised</c> (SosAlertRaisedEvent).</summary>
public record RecordSosRaisedCommand(
    Guid TripId,
    Guid RouteId,
    Guid? OrganizationId,
    string? RouteName,
    Guid DriverUserId,
    Guid VehicleId,
    double? Latitude,
    double? Longitude,
    int StudentsOnBoard,
    DateTimeOffset OccurredAt);

/// <summary>From <c>trip-tracking.sos-resolved</c>.</summary>
public record RecordSosResolvedCommand(
    Guid TripId,
    Guid RouteId,
    Guid? OrganizationId,
    Guid ResolvedBy,
    DateTimeOffset OccurredAt);

/// <summary>From <c>trip-tracking.trip-completed</c> (TripCompletedEvent).</summary>
public record RecordTripCompletedCommand(
    Guid TripId,
    Guid RouteId,
    Guid? OrganizationId,
    string? RouteName,
    int DeliveredStudents,
    int AbsentStudents,
    int IncidentCount,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public record AcknowledgeAlertCommand(Guid OrganizationId, Guid AlertId, Guid AdministratorUserId);

public record ResolveAlertCommand(Guid OrganizationId, Guid AlertId, Guid AdministratorUserId);
