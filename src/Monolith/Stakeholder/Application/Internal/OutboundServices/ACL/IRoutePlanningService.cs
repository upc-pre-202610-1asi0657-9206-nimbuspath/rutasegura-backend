using Monolith.Stakeholder.Domain.Model;

namespace Monolith.Stakeholder.Application.Internal.OutboundServices.ACL;

/// <summary>
///     Port towards Route Planning (Administration context). The adapter in Infrastructure translates it to the
///     published <c>IRoutePlanningContextFacade</c>; Application never sees Administration types.
/// </summary>
public interface IRoutePlanningService
{
    Task<RouteInfo?> FindRouteByEnrollmentCodeAsync(string enrollmentCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<RouteInfo>> FindRoutesAsync(IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StudentStop>> FindStudentStopsAsync(IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> FindRouteIdsOperatedByAsync(Guid organizationId, Guid driverUserId,
        CancellationToken cancellationToken);

    /// <summary>Places (or re-places) the child at the stop of its pickup address.</summary>
    Task<PlacementOutcome> PlaceStudentAsync(Guid organizationId, Guid routeId, Guid studentId, double latitude,
        double longitude, string address, CancellationToken cancellationToken);

    /// <summary>Takes the child out of the route; a route that no longer exists is not an error.</summary>
    Task<PlacementOutcome> RemoveStudentAsync(Guid organizationId, Guid routeId, Guid studentId,
        CancellationToken cancellationToken);
}

/// <param name="Direction">PICKUP | RETURN.</param>
/// <param name="Status">DRAFT | ACTIVE | INACTIVE.</param>
public sealed record RouteInfo(
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
    bool AcceptsEnrollments)
{
    /// <summary>Free seats when the route has a vehicle; null when there is none yet.</summary>
    public int? AvailableSeats => SeatingCapacity is { } seats ? Math.Max(0, seats - StudentCount) : null;
}

public sealed record StudentStop(
    Guid RouteId,
    Guid StudentId,
    Guid StopId,
    string StopName,
    string? StopAddress,
    int Sequence,
    double Latitude,
    double Longitude);

public sealed record PlacementOutcome(
    bool Succeeded,
    Guid? StopId,
    string? StopName,
    int? StopSequence,
    StakeholderError Error,
    string? Message)
{
    public static PlacementOutcome Failed(StakeholderError error, string? message) =>
        new(false, null, null, null, error, message);
}
