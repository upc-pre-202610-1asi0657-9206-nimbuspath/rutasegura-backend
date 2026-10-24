namespace Monolith.Administration.Interfaces.Rest.Resources;

// ── Requests ─────────────────────────────────────────────────────────────────

/// <summary>
///     US-04. <c>Direction</c>: PICKUP | RETURN. Times are local to <c>TimeZoneId</c> (default America/Lima).
///     <c>OperatingDays</c>: "Monday".."Sunday".
/// </summary>
public record CreateRouteResource(
    string Name,
    string Direction,
    TimeOnly DepartureTime,
    TimeOnly EstimatedArrivalTime,
    string? TimeZoneId,
    IReadOnlyCollection<DayOfWeek> OperatingDays);

public record RenameRouteResource(string Name);

public record RescheduleRouteResource(
    TimeOnly DepartureTime,
    TimeOnly EstimatedArrivalTime,
    string? TimeZoneId,
    IReadOnlyCollection<DayOfWeek> OperatingDays);

/// <summary>Where children are picked up (PICKUP route) or dropped off (RETURN route). <c>Position</c> is 1-based.</summary>
public record AddRouteStopResource(string Name, string? Address, double Latitude, double Longitude, int? Position);

public record UpdateRouteStopResource(string Name, string? Address, double Latitude, double Longitude);

public record ReorderRouteStopsResource(IReadOnlyList<Guid> StopIds);

/// <summary>US-05: driver and vehicle that operate the route.</summary>
public record AssignRouteResourcesResource(Guid DriverId, Guid VehicleId);

// ── Responses ────────────────────────────────────────────────────────────────

public record RouteScheduleResource(
    TimeOnly DepartureTime,
    TimeOnly EstimatedArrivalTime,
    string TimeZoneId,
    IReadOnlyList<DayOfWeek> OperatingDays);

public record RouteAssignmentResource(Guid DriverId, Guid VehicleId, DateTimeOffset AssignedAt);

public record RouteStopResource(
    Guid Id,
    int Sequence,
    string Name,
    string? Address,
    double Latitude,
    double Longitude,
    IReadOnlyList<Guid> StudentIds);

public record RouteResource(
    Guid Id,
    string Name,
    string Direction,
    string StopPurpose,
    string Status,
    RouteScheduleResource Schedule,
    RouteAssignmentResource? Assignment,
    int StudentCount,
    IReadOnlyList<RouteStopResource> Stops,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt);

public record ActiveRouteQuotaResource(
    string PlanCode,
    int ActiveRoutes,
    int MaxActiveRoutes,
    int RemainingActiveRoutes,
    bool LimitReached);
