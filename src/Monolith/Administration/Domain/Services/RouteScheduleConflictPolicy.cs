using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Model.ValueObjects;
// Microsoft.AspNetCore.Routing (implicit global using of the Web SDK) also defines Route.
using Route = Monolith.Administration.Domain.Model.Aggregates.Route;

namespace Monolith.Administration.Domain.Services;

/// <summary>
///     Domain service for US-05 S2: a driver or a vehicle cannot serve two routes whose schedules overlap.
/// </summary>
/// <remarks>
///     It needs other Route aggregates, so it does not belong to a single route. Only routes that reserve their
///     resources (DRAFT or ACTIVE with an assignment) are compared. The application layer runs it inside the
///     same transaction that saves the assignment; the repository adapter must lock the driver/vehicle rows so
///     concurrent requests cannot both pass the check.
/// </remarks>
public static class RouteScheduleConflictPolicy
{
    public static void EnsureNoConflict(Route route, RouteSchedule schedule, DriverId driverId, VehicleId vehicleId,
        IEnumerable<Route> otherRoutes)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(otherRoutes);

        foreach (var other in otherRoutes)
        {
            if (other.Id == route.Id || !other.ReservesResources()) continue;

            var sharesDriver = other.UsesDriver(driverId);
            var sharesVehicle = other.UsesVehicle(vehicleId);
            if (!sharesDriver && !sharesVehicle) continue;
            if (!schedule.OverlapsWith(other.Schedule)) continue;

            var resource = sharesDriver ? "driver" : "vehicle";
            throw new AdministrationDomainException(AdministrationError.ResourceScheduleConflict,
                $"The {resource} is already assigned to route '{other.Name}' " +
                $"({other.Schedule.DepartureTime:HH\\:mm}-{other.Schedule.EstimatedArrivalTime:HH\\:mm}) " +
                "on an overlapping day and time.");
        }
    }

    /// <summary>Convenience overload that validates the route's current schedule and assignment.</summary>
    public static void EnsureNoConflict(Route route, IEnumerable<Route> otherRoutes)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.Assignment is null) return;
        EnsureNoConflict(route, route.Schedule, route.Assignment.DriverId, route.Assignment.VehicleId, otherRoutes);
    }
}
