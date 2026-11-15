namespace Monolith.Administration.Application.IntegrationEvents.Outbound;

// ─────────────────────────────────────────────────────────────────────────────
// Contracts PUBLISHED by Administration (Published Language). Primitive types only, no domain classes.
// RoutePublishedV1 / RouteRetiredV1 mirror exactly what Trip & Tracking consumes
// (src/Services/Trip-Tracking/.../Messaging/Inbound/InboundContracts.cs):
//   administration.route-published  → first activation (or re-activation) of a route
//   administration.route-updated    → change of an ACTIVE route (same snapshot)
//   administration.route-retired    → the route stops operating
//   administration.route-assigned   → driver/vehicle assigned (RouteAssignedEvent of the report)
// Evolution rule: adding optional fields is compatible; renaming/removing requires SchemaVersion + 1.
// ─────────────────────────────────────────────────────────────────────────────

public static class AdministrationMessageTypes
{
    public const string Prefix = "administration.";
    public const string RoutePublished = Prefix + "route-published";
    public const string RouteUpdated = Prefix + "route-updated";
    public const string RouteRetired = Prefix + "route-retired";
    public const string RouteAssigned = Prefix + "route-assigned";
}

public sealed record RouteStopV1(Guid StopId, int Sequence, string Name, double Latitude, double Longitude);

public sealed record RouteStudentV1(Guid StudentId, Guid StopId);

/// <summary>
///     Full snapshot of an operating route. <c>DriverId</c> is the driver's IAM user id (Trip &amp; Tracking
///     matches it with the JWT of the mobile app); <c>DepartureTime</c> is HH:mm local to <c>TimeZoneId</c>;
///     <c>Direction</c> is "Pickup" | "Return"; <c>OperatingDays</c> are "Monday".."Sunday".
/// </summary>
public sealed record RoutePublishedV1(
    Guid RouteId,
    Guid? OrganizationId,
    string Name,
    string Direction,
    Guid DriverId,
    Guid VehicleId,
    string DepartureTime,
    string TimeZoneId,
    IReadOnlyList<string> OperatingDays,
    IReadOnlyList<RouteStopV1> Stops,
    IReadOnlyList<RouteStudentV1> Students);

public sealed record RouteRetiredV1(Guid RouteId);

/// <summary>Driver and vehicle assigned to a route; <c>DriverId</c> is the driver's IAM user id.</summary>
public sealed record RouteAssignedV1(
    Guid RouteId,
    Guid OrganizationId,
    string RouteName,
    string Direction,
    Guid DriverId,
    Guid VehicleId,
    string DepartureTime,
    string EstimatedArrivalTime,
    string TimeZoneId,
    IReadOnlyList<string> OperatingDays,
    bool RouteIsActive,
    DateTimeOffset AssignedAt);
