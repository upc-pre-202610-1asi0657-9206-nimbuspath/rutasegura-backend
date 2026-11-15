namespace Monolith.Administration.Interfaces.Rest.Resources;

public record OperationalAlertResource(
    Guid Id,
    string Type,
    string Severity,
    string Status,
    Guid TripId,
    Guid RouteId,
    string RouteName,
    Guid? IncidentId,
    string? Category,
    string? Detail,
    double? Latitude,
    double? Longitude,
    int AffectedStudents,
    Guid? DriverUserId,
    Guid? VehicleId,
    DateTimeOffset OccurredAt,
    DateTimeOffset? AcknowledgedAt,
    Guid? AcknowledgedBy,
    DateTimeOffset? ResolvedAt);

public record TripRecordResource(
    Guid Id,
    Guid TripId,
    Guid RouteId,
    string RouteName,
    int DeliveredStudents,
    int AbsentStudents,
    int IncidentCount,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    double DurationMinutes,
    double AttendanceRate);
