namespace Monolith.Administration.Domain.Model.Commands;

// Route Planning commands (routes, stops, students per stop, assignment and lifecycle).

/// <summary>US-04: creates a route in DRAFT. <paramref name="Direction" /> is PICKUP or RETURN.</summary>
public record CreateRouteCommand(
    Guid OrganizationId,
    string Name,
    string Direction,
    TimeOnly DepartureTime,
    TimeOnly EstimatedArrivalTime,
    string TimeZoneId,
    IReadOnlyCollection<DayOfWeek> OperatingDays);

public record RenameRouteCommand(Guid OrganizationId, Guid RouteId, string Name);

public record RescheduleRouteCommand(
    Guid OrganizationId,
    Guid RouteId,
    TimeOnly DepartureTime,
    TimeOnly EstimatedArrivalTime,
    string TimeZoneId,
    IReadOnlyCollection<DayOfWeek> OperatingDays);

/// <summary>Adds the place where children are picked up or dropped off. <paramref name="Position" /> is 1-based.</summary>
public record AddRouteStopCommand(
    Guid OrganizationId,
    Guid RouteId,
    string Name,
    string? Address,
    double Latitude,
    double Longitude,
    int? Position);

public record UpdateRouteStopCommand(
    Guid OrganizationId,
    Guid RouteId,
    Guid StopId,
    string Name,
    string? Address,
    double Latitude,
    double Longitude);

public record RemoveRouteStopCommand(Guid OrganizationId, Guid RouteId, Guid StopId);

public record ReorderRouteStopsCommand(Guid OrganizationId, Guid RouteId, IReadOnlyList<Guid> StopIds);

/// <summary>US-04 S2: sets the stop where a student is picked up / dropped off.</summary>
public record AssignStudentToStopCommand(Guid OrganizationId, Guid RouteId, Guid StopId, Guid StudentId);

public record UnassignStudentFromRouteCommand(Guid OrganizationId, Guid RouteId, Guid StudentId);

/// <summary>US-05: assigns the driver and the vehicle that operate the route.</summary>
public record AssignRouteResourcesCommand(Guid OrganizationId, Guid RouteId, Guid DriverId, Guid VehicleId);

public record ReleaseRouteResourcesCommand(Guid OrganizationId, Guid RouteId);

/// <summary>Puts the route into operation; subject to the active-route limit of the plan.</summary>
public record ActivateRouteCommand(Guid OrganizationId, Guid RouteId);

public record DeactivateRouteCommand(Guid OrganizationId, Guid RouteId);

public record DeleteRouteCommand(Guid OrganizationId, Guid RouteId);

/// <summary>
///     Families: places a child enrolled by its parent at the stop of its pickup address (creating the stop when
///     there is none nearby). Sent by the Stakeholder bounded context through <c>IRoutePlanningContextFacade</c>.
/// </summary>
public record PlaceStudentOnRouteCommand(
    Guid OrganizationId,
    Guid RouteId,
    Guid StudentId,
    double Latitude,
    double Longitude,
    string? Address);

/// <summary>Families: the enrollment ended; the child leaves the route (idempotent).</summary>
public record RemoveStudentFromRouteCommand(Guid OrganizationId, Guid RouteId, Guid StudentId);

/// <summary>Gives the route a new enrollment code (e.g. the previous one was shared by mistake).</summary>
public record RegenerateRouteEnrollmentCodeCommand(Guid OrganizationId, Guid RouteId);
