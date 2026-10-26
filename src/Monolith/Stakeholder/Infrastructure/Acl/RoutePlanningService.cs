using Monolith.Administration.Interfaces.ACL;
using Monolith.Stakeholder.Application.Internal.OutboundServices.ACL;
using Monolith.Stakeholder.Domain.Model;

namespace Monolith.Stakeholder.Infrastructure.Acl;

/// <summary>
///     Anti-Corruption Layer towards Route Planning: translates the published facade of Administration (primitives and
///     error names) into the port Stakeholder's application layer speaks.
/// </summary>
public class RoutePlanningService(IRoutePlanningContextFacade facade) : IRoutePlanningService
{
    public async Task<RouteInfo?> FindRouteByEnrollmentCodeAsync(string enrollmentCode,
        CancellationToken cancellationToken)
    {
        var route = await facade.FindRouteByEnrollmentCodeAsync(enrollmentCode, cancellationToken);
        return route is null ? null : ToRouteInfo(route);
    }

    public async Task<IReadOnlyList<RouteInfo>> FindRoutesAsync(IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken) =>
        routeIds.Count == 0
            ? []
            : (await facade.FindRoutesAsync(routeIds, cancellationToken)).Select(ToRouteInfo).ToList();

    public async Task<IReadOnlyList<StudentStop>> FindStudentStopsAsync(IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken) =>
        routeIds.Count == 0
            ? []
            : (await facade.FindPlannedStudentsAsync(routeIds, cancellationToken))
            .Select(s => new StudentStop(s.RouteId, s.StudentId, s.StopId, s.StopName, s.StopAddress, s.Sequence,
                s.Latitude, s.Longitude))
            .ToList();

    public Task<IReadOnlyList<Guid>> FindRouteIdsOperatedByAsync(Guid organizationId, Guid driverUserId,
        CancellationToken cancellationToken) =>
        facade.FindRouteIdsOperatedByAsync(organizationId, driverUserId, cancellationToken);

    public async Task<PlacementOutcome> PlaceStudentAsync(Guid organizationId, Guid routeId, Guid studentId,
        double latitude, double longitude, string address, CancellationToken cancellationToken) =>
        ToOutcome(await facade.PlaceStudentAsync(organizationId, routeId, studentId, latitude, longitude, address,
            cancellationToken));

    public async Task<PlacementOutcome> RemoveStudentAsync(Guid organizationId, Guid routeId, Guid studentId,
        CancellationToken cancellationToken)
    {
        var outcome = await facade.RemoveStudentAsync(organizationId, routeId, studentId, cancellationToken);
        // The route was deleted: there is no seat to free, the enrollment can end anyway.
        if (!outcome.Succeeded && outcome.Error == "RouteNotFound")
            return new PlacementOutcome(true, null, null, null, StakeholderError.None, null);
        return ToOutcome(outcome);
    }

    private static PlacementOutcome ToOutcome(StudentPlacement placement) =>
        placement.Succeeded
            ? new PlacementOutcome(true, placement.StopId, placement.StopName, placement.StopSequence,
                StakeholderError.None, null)
            : PlacementOutcome.Failed(TranslateError(placement.Error), placement.Message);

    /// <summary>Administration error names → Stakeholder errors (the only place that knows both vocabularies).</summary>
    private static StakeholderError TranslateError(string? administrationError) => administrationError switch
    {
        "VehicleCapacityExceeded" => StakeholderError.RouteFull,
        "RouteStopLimitReached" => StakeholderError.RouteStopLimitReached,
        "RouteNotAcceptingEnrollments" => StakeholderError.RouteNotAcceptingEnrollments,
        "RouteNotFound" => StakeholderError.RouteNotFound,
        "InvalidAdministrationData" or "RouteStopTooClose" => StakeholderError.InvalidStakeholderData,
        "OperationCancelled" => StakeholderError.OperationCancelled,
        _ => StakeholderError.RoutePlanningUnavailable
    };

    private static RouteInfo ToRouteInfo(RouteSummary route) =>
        new(route.RouteId, route.OrganizationId, route.Name, route.Direction, route.Status, route.DepartureTime,
            route.EstimatedArrivalTime, route.TimeZoneId, route.OperatingDays, route.StudentCount,
            route.SeatingCapacity, route.AcceptsEnrollments);
}
