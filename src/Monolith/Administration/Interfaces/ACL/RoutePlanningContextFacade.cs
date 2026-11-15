using Monolith.Administration.Application.CommandServices;
using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Model.Commands;
using Monolith.Administration.Domain.Model.Queries;
using Monolith.Administration.Domain.Model.ValueObjects;
// Microsoft.AspNetCore.Routing (implicit global using of the Web SDK) also defines Route.
using Route = Monolith.Administration.Domain.Model.Aggregates.Route;

namespace Monolith.Administration.Interfaces.ACL;

/// <summary>Adapter of the published facade over the Route Planning application services.</summary>
public class RoutePlanningContextFacade(
    IRoutePlanningCommandService commandService,
    IRoutePlanningQueryService queryService,
    IRosterQueryService rosterQueryService) : IRoutePlanningContextFacade
{
    public async Task<RouteSummary?> FindRouteByEnrollmentCodeAsync(string enrollmentCode,
        CancellationToken cancellationToken)
    {
        var route = await queryService.Handle(new GetRouteByEnrollmentCodeQuery(enrollmentCode), cancellationToken);
        return route is null ? null : await ToSummaryAsync(route, cancellationToken);
    }

    public async Task<IReadOnlyList<RouteSummary>> FindRoutesAsync(IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken)
    {
        var routes = await queryService.Handle(new GetRoutesByIdsQuery(routeIds), cancellationToken);
        var result = new List<RouteSummary>();
        foreach (var route in routes) result.Add(await ToSummaryAsync(route, cancellationToken));
        return result;
    }

    public async Task<IReadOnlyList<PlannedStudentStop>> FindPlannedStudentsAsync(IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken)
    {
        var routes = await queryService.Handle(new GetRoutesByIdsQuery(routeIds), cancellationToken);
        return routes.SelectMany(route => route.Stops.SelectMany(stop => stop.Students.Select(student =>
                new PlannedStudentStop(route.Id.Identifier, student.Identifier, stop.Id.Identifier, stop.Name,
                    stop.Address, stop.Sequence, stop.Location.Latitude, stop.Location.Longitude))))
            .ToList();
    }

    public async Task<IReadOnlyList<Guid>> FindRouteIdsOperatedByAsync(Guid organizationId, Guid driverUserId,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || driverUserId == Guid.Empty) return [];
        var driver = await rosterQueryService.Handle(new GetDriverByUserIdQuery(organizationId, driverUserId),
            cancellationToken);
        if (driver is null) return [];
        var routes = await queryService.Handle(new GetRoutesByDriverQuery(organizationId, driver.Id.Identifier),
            cancellationToken);
        return routes.Select(route => route.Id.Identifier).ToList();
    }

    public async Task<StudentPlacement> PlaceStudentAsync(Guid organizationId, Guid routeId, Guid studentId,
        double latitude, double longitude, string? address, CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(
            new PlaceStudentOnRouteCommand(organizationId, routeId, studentId, latitude, longitude, address),
            cancellationToken);
        if (!result.IsSuccess) return StudentPlacement.Failed(result.Error?.ToString() ?? "Unknown", result.Message);

        var stop = result.Value!.StopOf(new StudentId(studentId));
        return new StudentPlacement(true, stop?.Id.Identifier, stop?.Name, stop?.Sequence, null, null);
    }

    public async Task<StudentPlacement> RemoveStudentAsync(Guid organizationId, Guid routeId, Guid studentId,
        CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new RemoveStudentFromRouteCommand(organizationId, routeId, studentId),
            cancellationToken);
        return result.IsSuccess
            ? new StudentPlacement(true, null, null, null, null, null)
            : StudentPlacement.Failed(result.Error?.ToString() ?? "Unknown", result.Message);
    }

    private async Task<RouteSummary> ToSummaryAsync(Route route, CancellationToken cancellationToken)
    {
        int? seats = null;
        if (route.Assignment is { } assignment)
            seats = (await rosterQueryService.Handle(
                new GetVehicleByIdQuery(route.OrganizationId.Identifier, assignment.VehicleId.Identifier),
                cancellationToken))?.Capacity.Value;

        return new RouteSummary(route.Id.Identifier, route.OrganizationId.Identifier, route.Name.Value,
            route.Direction.Value, route.Status.Value, route.Schedule.DepartureTime,
            route.Schedule.EstimatedArrivalTime, route.Schedule.TimeZoneId,
            route.Schedule.OperatingDays.Select(day => day.ToString()).ToList(), route.StudentCount, seats,
            route.AcceptsEnrollments());
    }
}
