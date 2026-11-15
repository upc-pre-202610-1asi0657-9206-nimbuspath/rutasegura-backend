using Monolith.Administration.Domain.Model.Commands;

namespace Monolith.Administration.Application.IntegrationEvents.Inbound;

// ─────────────────────────────────────────────────────────────────────────────
// Contracts CONSUMED by Administration, published by Trip & Tracking on the topic exchange
// "trip-tracking.events" (src/Services/Trip-Tracking/.../IntegrationEvents/TripIntegrationEvents.cs).
// They are copied on purpose (no project reference between services): only the fields Administration
// needs are relevant, extra fields are ignored by the JSON reader (tolerant reader).
//
//   Report name             Routing key / message type
//   IncidentReportedEvent → trip-tracking.incident-reported   (IncidentReportedV1)
//   SosAlertRaisedEvent   → trip-tracking.sos-raised          (SosRaisedV1)
//   (resolution)          → trip-tracking.sos-resolved        (SosResolvedV1)
//   TripCompletedEvent    → trip-tracking.trip-completed      (TripCompletedV1)
// ─────────────────────────────────────────────────────────────────────────────

public static class TripTrackingMessageTypes
{
    public const string Prefix = "trip-tracking.";
    public const string IncidentReported = Prefix + "incident-reported";
    public const string SosRaised = Prefix + "sos-raised";
    public const string SosResolved = Prefix + "sos-resolved";
    public const string TripCompleted = Prefix + "trip-completed";

    /// <summary>Message types Administration subscribes to.</summary>
    public static readonly IReadOnlyList<string> Subscribed = [IncidentReported, SosRaised, SosResolved, TripCompleted];
}

public sealed record GeoPointV1(double Latitude, double Longitude);

public sealed record IncidentReportedV1(
    Guid TripId,
    Guid RouteId,
    Guid? OrganizationId,
    string RouteName,
    Guid IncidentId,
    string Type,
    string? Detail,
    bool NotifyGuardians,
    GeoPointV1? Position,
    IReadOnlyList<Guid>? AffectedStudents,
    DateTimeOffset OccurredAt)
{
    public RecordIncidentReportedCommand ToCommand() => new(TripId, RouteId, OrganizationId, RouteName, IncidentId,
        Type, Detail, Position?.Latitude, Position?.Longitude, AffectedStudents?.Count ?? 0, OccurredAt);
}

public sealed record SosRaisedV1(
    Guid TripId,
    Guid RouteId,
    Guid? OrganizationId,
    string RouteName,
    Guid DriverId,
    Guid VehicleId,
    GeoPointV1? Position,
    IReadOnlyList<Guid>? StudentsOnBoard,
    DateTimeOffset OccurredAt)
{
    /// <remarks><c>DriverId</c> in Trip &amp; Tracking is the driver's IAM user id.</remarks>
    public RecordSosRaisedCommand ToCommand() => new(TripId, RouteId, OrganizationId, RouteName, DriverId, VehicleId,
        Position?.Latitude, Position?.Longitude, StudentsOnBoard?.Count ?? 0, OccurredAt);
}

public sealed record SosResolvedV1(
    Guid TripId,
    Guid RouteId,
    Guid? OrganizationId,
    Guid ResolvedBy,
    DateTimeOffset OccurredAt)
{
    public RecordSosResolvedCommand ToCommand() => new(TripId, RouteId, OrganizationId, ResolvedBy, OccurredAt);
}

public sealed record TripCompletedV1(
    Guid TripId,
    Guid RouteId,
    Guid? OrganizationId,
    string RouteName,
    IReadOnlyList<Guid>? DeliveredStudents,
    IReadOnlyList<Guid>? AbsentStudents,
    int IncidentCount,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
{
    public RecordTripCompletedCommand ToCommand() => new(TripId, RouteId, OrganizationId, RouteName,
        DeliveredStudents?.Count ?? 0, AbsentStudents?.Count ?? 0, IncidentCount, StartedAt, CompletedAt);
}
