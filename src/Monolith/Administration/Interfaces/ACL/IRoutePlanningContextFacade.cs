namespace Monolith.Administration.Interfaces.ACL;

/// <summary>
///     Published facade of Route Planning (Anti-Corruption Layer). The Stakeholder bounded context (parents and their
///     children) uses only this contract to find the route a family received a code for and to place or remove a
///     child on it; it never touches the Route aggregate, its repositories or its tables.
/// </summary>
/// <remarks>
///     Placing or removing a child goes through the Route Planning use cases, so the route keeps its invariants
///     (aforo, stop limit) and an ACTIVE route publishes <c>administration.route-updated</c> for Trip &amp; Tracking.
/// </remarks>
public interface IRoutePlanningContextFacade
{
    /// <summary>The route that uses <paramref name="enrollmentCode" />, or null.</summary>
    Task<RouteSummary?> FindRouteByEnrollmentCodeAsync(string enrollmentCode, CancellationToken cancellationToken);

    /// <summary>Routes with the given ids (deleted routes are simply missing).</summary>
    Task<IReadOnlyList<RouteSummary>> FindRoutesAsync(IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken);

    /// <summary>Where each student currently is on the given routes (the administrator may have moved them).</summary>
    Task<IReadOnlyList<PlannedStudentStop>> FindPlannedStudentsAsync(IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken);

    /// <summary>Routes (any status) assigned to the driver whose Iam user id is <paramref name="driverUserId" />.</summary>
    Task<IReadOnlyList<Guid>> FindRouteIdsOperatedByAsync(Guid organizationId, Guid driverUserId,
        CancellationToken cancellationToken);

    /// <summary>Places the student at the stop of its pickup address (or moves it there after the address changed).</summary>
    Task<StudentPlacement> PlaceStudentAsync(Guid organizationId, Guid routeId, Guid studentId, double latitude,
        double longitude, string? address, CancellationToken cancellationToken);

    /// <summary>Takes the student out of the route; succeeds when it was not planned anymore.</summary>
    Task<StudentPlacement> RemoveStudentAsync(Guid organizationId, Guid routeId, Guid studentId,
        CancellationToken cancellationToken);
}

/// <summary>Primitive snapshot of a route (Published Language: no Administration domain types).</summary>
/// <param name="Direction">PICKUP | RETURN.</param>
/// <param name="Status">DRAFT | ACTIVE | INACTIVE.</param>
/// <param name="SeatingCapacity">Seats of the assigned vehicle, when the route has one.</param>
public sealed record RouteSummary(
    Guid RouteId,
    Guid OrganizationId,
    string Name,
    string Direction,
    string Status,
    TimeOnly DepartureTime,
    TimeOnly EstimatedArrivalTime,
    string TimeZoneId,
    IReadOnlyList<string> OperatingDays,
    int StudentCount,
    int? SeatingCapacity,
    bool AcceptsEnrollments);

/// <summary>Stop of a student on a route.</summary>
public sealed record PlannedStudentStop(
    Guid RouteId,
    Guid StudentId,
    Guid StopId,
    string StopName,
    string? StopAddress,
    int Sequence,
    double Latitude,
    double Longitude);

/// <summary>Outcome of placing / removing a student. <c>Error</c> is the name of the Administration error.</summary>
public sealed record StudentPlacement(
    bool Succeeded,
    Guid? StopId,
    string? StopName,
    int? StopSequence,
    string? Error,
    string? Message)
{
    public static StudentPlacement Failed(string error, string? message) => new(false, null, null, null, error, message);
}
