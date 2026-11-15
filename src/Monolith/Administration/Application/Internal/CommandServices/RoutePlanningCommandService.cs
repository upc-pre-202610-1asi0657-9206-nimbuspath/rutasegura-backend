using Monolith.Administration.Application.CommandServices;
using Monolith.Administration.Application.IntegrationEvents.Outbound;
using Monolith.Administration.Application.Internal.OutboundServices;
using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Commands;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Repositories;
using Monolith.Administration.Domain.Services;
using Monolith.Shared.Application.Model;
using Monolith.Shared.Domain.Repositories;
// Microsoft.AspNetCore.Routing (implicit global using of the Web SDK) also defines Route and RouteDirection.
using Route = Monolith.Administration.Domain.Model.Aggregates.Route;
using RouteDirection = Monolith.Administration.Domain.Model.ValueObjects.RouteDirection;

namespace Monolith.Administration.Application.Internal.CommandServices;

/// <remarks>
///     Every use case that changes a route translates its domain events into integration events and stores them
///     in the outbox before <see cref="IUnitOfWork.CompleteAsync" />, so the business change, and the message
///     Trip &amp; Tracking receives, are committed together.
/// </remarks>
public class RoutePlanningCommandService(
    IRouteRepository routeRepository,
    IDriverRepository driverRepository,
    IVehicleRepository vehicleRepository,
    IPlanLimitsProvider planLimitsProvider,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRoutePlanningCommandService
{
    // ── Route details ────────────────────────────────────────────────────────

    public Task<Result<Route>> Handle(CreateRouteCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Route>(async () =>
        {
            var organizationId = AdministrationOperation.Organization(command.OrganizationId);
            var name = new RouteName(command.Name);
            if (await routeRepository.ExistsByNameAsync(organizationId, name, cancellationToken))
                return RouteNameTaken(name);

            var route = new Route(organizationId, name, new RouteDirection(command.Direction),
                new RouteSchedule(command.DepartureTime, command.EstimatedArrivalTime, command.TimeZoneId,
                    command.OperatingDays));

            await routeRepository.AddAsync(route, cancellationToken);
            await SaveAsync(route, null, cancellationToken);
            return Result<Route>.Success(route);
        });

    public Task<Result<Route>> Handle(RenameRouteCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Route>(async () =>
        {
            var route = await FindRouteAsync(command.OrganizationId, command.RouteId, cancellationToken);
            if (route is null) return RouteNotFound();

            var name = new RouteName(command.Name);
            if (name != route.Name && await routeRepository.ExistsByNameAsync(route.OrganizationId, name,
                    cancellationToken))
                return RouteNameTaken(name);

            route.Rename(name);
            return await UpdateAsync(route, null, cancellationToken);
        });

    /// <summary>A new schedule must not overlap with other routes of the same driver or vehicle.</summary>
    public Task<Result<Route>> Handle(RescheduleRouteCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Route>(async () =>
        {
            var route = await FindRouteAsync(command.OrganizationId, command.RouteId, cancellationToken);
            if (route is null) return RouteNotFound();

            var schedule = new RouteSchedule(command.DepartureTime, command.EstimatedArrivalTime,
                command.TimeZoneId, command.OperatingDays);
            if (route.Assignment is { } assignment)
                RouteScheduleConflictPolicy.EnsureNoConflict(route, schedule, assignment.DriverId,
                    assignment.VehicleId,
                    await RoutesSharingResourcesAsync(assignment.DriverId, assignment.VehicleId, cancellationToken));

            route.Reschedule(schedule);
            return await UpdateAsync(route, null, cancellationToken);
        });

    // ── Stops (where each child is picked up / dropped off) ──────────────────

    public Task<Result<Route>> Handle(AddRouteStopCommand command, CancellationToken cancellationToken) =>
        ChangeRouteAsync(command.OrganizationId, command.RouteId,
            route => route.AddStop(command.Name, command.Address,
                new GeoLocation(command.Latitude, command.Longitude), command.Position), cancellationToken);

    public Task<Result<Route>> Handle(UpdateRouteStopCommand command, CancellationToken cancellationToken) =>
        ChangeRouteAsync(command.OrganizationId, command.RouteId,
            route => route.UpdateStop(new RouteStopId(command.StopId), command.Name, command.Address,
                new GeoLocation(command.Latitude, command.Longitude)), cancellationToken);

    public Task<Result<Route>> Handle(RemoveRouteStopCommand command, CancellationToken cancellationToken) =>
        ChangeRouteAsync(command.OrganizationId, command.RouteId,
            route => route.RemoveStop(new RouteStopId(command.StopId)), cancellationToken);

    public Task<Result<Route>> Handle(ReorderRouteStopsCommand command, CancellationToken cancellationToken) =>
        ChangeRouteAsync(command.OrganizationId, command.RouteId,
            route => route.ReorderStops((command.StopIds ?? Array.Empty<Guid>()).Select(id => new RouteStopId(id)).ToList()),
            cancellationToken);

    // ── Students per stop ────────────────────────────────────────────────────

    /// <summary>When the route already has a vehicle, its seating capacity limits the planned students.</summary>
    public Task<Result<Route>> Handle(AssignStudentToStopCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Route>(async () =>
        {
            var route = await FindRouteAsync(command.OrganizationId, command.RouteId, cancellationToken);
            if (route is null) return RouteNotFound();

            SeatingCapacity? capacity = null;
            if (route.Assignment is { } assignment)
                capacity = (await vehicleRepository.FindByVehicleIdAsync(assignment.VehicleId, cancellationToken))
                    ?.Capacity;

            route.AssignStudentToStop(new RouteStopId(command.StopId), new StudentId(command.StudentId), capacity);
            return await UpdateAsync(route, null, cancellationToken);
        });

    public Task<Result<Route>> Handle(UnassignStudentFromRouteCommand command, CancellationToken cancellationToken) =>
        ChangeRouteAsync(command.OrganizationId, command.RouteId,
            route => route.UnassignStudent(new StudentId(command.StudentId)), cancellationToken);

    // ── Assignment (US-05) ───────────────────────────────────────────────────

    public Task<Result<Route>> Handle(AssignRouteResourcesCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Route>(async () =>
        {
            var route = await FindRouteAsync(command.OrganizationId, command.RouteId, cancellationToken);
            if (route is null) return RouteNotFound();

            var driver = await driverRepository.FindByDriverIdAsync(new DriverId(command.DriverId), cancellationToken);
            if (driver is null || driver.OrganizationId != route.OrganizationId)
                return Result<Route>.Failure(AdministrationError.DriverNotFound, "Driver was not found.");

            var vehicle = await vehicleRepository.FindByVehicleIdAsync(new VehicleId(command.VehicleId),
                cancellationToken);
            if (vehicle is null || vehicle.OrganizationId != route.OrganizationId)
                return Result<Route>.Failure(AdministrationError.VehicleNotFound, "Vehicle was not found.");

            RouteScheduleConflictPolicy.EnsureNoConflict(route, route.Schedule, driver.Id, vehicle.Id,
                await RoutesSharingResourcesAsync(driver.Id, vehicle.Id, cancellationToken));

            route.AssignResources(driver, vehicle, clock.GetUtcNow());
            return await UpdateAsync(route, driver, cancellationToken);
        });

    public Task<Result<Route>> Handle(ReleaseRouteResourcesCommand command, CancellationToken cancellationToken) =>
        ChangeRouteAsync(command.OrganizationId, command.RouteId, route => route.ReleaseResources(),
            cancellationToken);

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>
    ///     Activation re-validates the assigned driver and vehicle, checks schedule conflicts (a re-activated
    ///     route may collide with routes planned meanwhile) and enforces the active-route limit of the plan.
    /// </summary>
    public Task<Result<Route>> Handle(ActivateRouteCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Route>(async () =>
        {
            var route = await FindRouteAsync(command.OrganizationId, command.RouteId, cancellationToken);
            if (route is null) return RouteNotFound();
            if (route.Assignment is not { } assignment)
                return Result<Route>.Failure(AdministrationError.RouteNotReadyForActivation,
                    "The route needs a driver and a vehicle before it can be activated.");

            var driver = await driverRepository.FindByDriverIdAsync(assignment.DriverId, cancellationToken);
            if (driver is null)
                return Result<Route>.Failure(AdministrationError.DriverNotFound, "The assigned driver was not found.");
            var vehicle = await vehicleRepository.FindByVehicleIdAsync(assignment.VehicleId, cancellationToken);
            if (vehicle is null)
                return Result<Route>.Failure(AdministrationError.VehicleNotFound,
                    "The assigned vehicle was not found.");

            RouteScheduleConflictPolicy.EnsureNoConflict(route,
                await RoutesSharingResourcesAsync(assignment.DriverId, assignment.VehicleId, cancellationToken));

            var activeRoutes = await routeRepository.CountActiveByOrganizationIdAsync(route.OrganizationId,
                cancellationToken);
            var limits = await planLimitsProvider.GetCurrentLimitsAsync(route.OrganizationId, cancellationToken);

            route.Activate(driver, vehicle, activeRoutes, limits, clock.GetUtcNow());
            return await UpdateAsync(route, driver, cancellationToken);
        });

    public Task<Result<Route>> Handle(DeactivateRouteCommand command, CancellationToken cancellationToken) =>
        ChangeRouteAsync(command.OrganizationId, command.RouteId, route => route.Deactivate(), cancellationToken);

    public Task<Result<Route>> Handle(DeleteRouteCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Route>(async () =>
        {
            var route = await FindRouteAsync(command.OrganizationId, command.RouteId, cancellationToken);
            if (route is null) return RouteNotFound();

            route.EnsureCanBeDeleted();
            route.ClearDomainEvents();
            routeRepository.Remove(route);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<Route>.Success(route);
        });

    // ── Families (enrollments made by parents in the Stakeholder context) ─────

    /// <summary>The vehicle capacity (aforo) applies as when the administrator plans a student.</summary>
    public Task<Result<Route>> Handle(PlaceStudentOnRouteCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Route>(async () =>
        {
            var route = await FindRouteAsync(command.OrganizationId, command.RouteId, cancellationToken);
            if (route is null) return RouteNotFound();

            SeatingCapacity? capacity = null;
            if (route.Assignment is { } assignment)
                capacity = (await vehicleRepository.FindByVehicleIdAsync(assignment.VehicleId, cancellationToken))
                    ?.Capacity;

            route.PlaceStudent(new StudentId(command.StudentId), new GeoLocation(command.Latitude, command.Longitude),
                command.Address, capacity);
            return await UpdateAsync(route, null, cancellationToken);
        });

    public Task<Result<Route>> Handle(RemoveStudentFromRouteCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Route>(async () =>
        {
            var route = await FindRouteAsync(command.OrganizationId, command.RouteId, cancellationToken);
            if (route is null) return RouteNotFound();

            if (!route.RemoveStudent(new StudentId(command.StudentId))) return Result<Route>.Success(route);
            return await UpdateAsync(route, null, cancellationToken);
        });

    public Task<Result<Route>> Handle(RegenerateRouteEnrollmentCodeCommand command,
        CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Route>(async () =>
        {
            var route = await FindRouteAsync(command.OrganizationId, command.RouteId, cancellationToken);
            if (route is null) return RouteNotFound();

            // 31^8 codes: a collision is very unlikely, but the unique index must never be the one to find it.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                route.RegenerateEnrollmentCode();
                if (await routeRepository.FindByEnrollmentCodeAsync(route.EnrollmentCode, cancellationToken) is null)
                    break;
            }

            return await UpdateAsync(route, null, cancellationToken);
        });

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task<Result<Route>> ChangeRouteAsync(Guid organizationId, Guid routeId, Action<Route> change,
        CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Route>(async () =>
        {
            var route = await FindRouteAsync(organizationId, routeId, cancellationToken);
            if (route is null) return RouteNotFound();

            change(route);
            return await UpdateAsync(route, null, cancellationToken);
        });

    private async Task<Result<Route>> UpdateAsync(Route route, Driver? knownDriver,
        CancellationToken cancellationToken)
    {
        routeRepository.Update(route);
        await SaveAsync(route, knownDriver, cancellationToken);
        return Result<Route>.Success(route);
    }

    /// <summary>Domain events → outbox, then a single commit.</summary>
    private async Task SaveAsync(Route route, Driver? knownDriver, CancellationToken cancellationToken)
    {
        var domainEvents = route.DomainEvents.ToList();
        if (domainEvents.Count > 0)
        {
            Guid? driverUserId = null;
            if (route.Assignment is { } assignment &&
                RouteIntegrationEventMapper.RequiresDriverUserId(route, domainEvents))
            {
                var driver = knownDriver is not null && knownDriver.Id == assignment.DriverId
                    ? knownDriver
                    : await driverRepository.FindByDriverIdAsync(assignment.DriverId, cancellationToken);
                driverUserId = driver?.UserId.Identifier;
            }

            foreach (var integrationEvent in RouteIntegrationEventMapper.Map(route, domainEvents, driverUserId,
                         clock.GetUtcNow()))
                await outbox.EnqueueAsync(integrationEvent, cancellationToken);

            route.ClearDomainEvents();
        }

        await unitOfWork.CompleteAsync(cancellationToken);
    }

    /// <summary>Routes of other organizations are reported as not found, so their existence is not disclosed.</summary>
    private async Task<Route?> FindRouteAsync(Guid organizationId, Guid routeId, CancellationToken cancellationToken)
    {
        var organization = AdministrationOperation.Organization(organizationId);
        var route = await routeRepository.FindByRouteIdAsync(new RouteId(routeId), cancellationToken);
        return route is not null && route.OrganizationId == organization ? route : null;
    }

    private async Task<IReadOnlyList<Route>> RoutesSharingResourcesAsync(DriverId driverId, VehicleId vehicleId,
        CancellationToken cancellationToken)
    {
        var byDriver = await routeRepository.FindAllByDriverIdAsync(driverId, cancellationToken);
        var byVehicle = await routeRepository.FindAllByVehicleIdAsync(vehicleId, cancellationToken);
        return byDriver.Concat(byVehicle).DistinctBy(route => route.Id).ToList();
    }

    private static Result<Route> RouteNotFound() =>
        Result<Route>.Failure(AdministrationError.RouteNotFound, "Route was not found.");

    private static Result<Route> RouteNameTaken(RouteName name) =>
        Result<Route>.Failure(AdministrationError.RouteNameAlreadyExists,
            $"The organization already has a route named '{name}'.");
}
