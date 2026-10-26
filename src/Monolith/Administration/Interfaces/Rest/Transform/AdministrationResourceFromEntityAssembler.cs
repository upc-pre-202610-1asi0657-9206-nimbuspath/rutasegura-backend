using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Entities;
using Monolith.Administration.Interfaces.Rest.Resources;
// Microsoft.AspNetCore.Routing (implicit global using of the Web SDK) also defines Route.
using Route = Monolith.Administration.Domain.Model.Aggregates.Route;

namespace Monolith.Administration.Interfaces.Rest.Transform;

public static class AdministrationResourceFromEntityAssembler
{
    public static DriverResource ToResourceFromEntity(Driver driver) =>
        new(driver.Id.Identifier,
            driver.UserId.Identifier,
            driver.FullName.FirstName,
            driver.FullName.LastName,
            driver.DocumentNumber.Value,
            driver.License.Number,
            driver.License.Category,
            driver.License.ExpiresOn,
            driver.PhoneNumber.Value,
            driver.Status.Value,
            driver.VerifiedAt,
            driver.CreatedAt);

    public static VehicleResource ToResourceFromEntity(Vehicle vehicle) =>
        new(vehicle.Id.Identifier,
            vehicle.LicensePlate.Display,
            vehicle.Capacity.Value,
            vehicle.Model.Brand,
            vehicle.Model.Model,
            vehicle.Status.Value,
            vehicle.CreatedAt);

    public static RouteResource ToResourceFromEntity(Route route) =>
        new(route.Id.Identifier,
            route.Name.Value,
            route.Direction.Value,
            route.Direction.StopPurpose,
            route.Status.Value,
            new RouteScheduleResource(route.Schedule.DepartureTime, route.Schedule.EstimatedArrivalTime,
                route.Schedule.TimeZoneId, route.Schedule.OperatingDays),
            route.Assignment is null
                ? null
                : new RouteAssignmentResource(route.Assignment.DriverId.Identifier,
                    route.Assignment.VehicleId.Identifier, route.Assignment.AssignedAt),
            route.StudentCount,
            route.Stops.Select(ToResourceFromEntity).ToList(),
            route.ActivatedAt,
            route.CreatedAt,
            route.EnrollmentCode.Value);

    public static RouteStopResource ToResourceFromEntity(RouteStop stop) =>
        new(stop.Id.Identifier,
            stop.Sequence,
            stop.Name,
            stop.Address,
            stop.Location.Latitude,
            stop.Location.Longitude,
            stop.Students.Select(student => student.Identifier).ToList(),
            stop.Origin.Value);

    public static ActiveRouteQuotaResource ToResourceFromModel(ActiveRouteQuota quota) =>
        new(quota.PlanCode, quota.ActiveRoutes, quota.MaxActiveRoutes, quota.RemainingActiveRoutes,
            quota.LimitReached);

    public static OperationalAlertResource ToResourceFromEntity(OperationalAlert alert) =>
        new(alert.Id.Identifier,
            alert.Type.Value,
            alert.Severity.Value,
            alert.Status.Value,
            alert.TripId.Identifier,
            alert.RouteId.Identifier,
            alert.RouteName,
            alert.IncidentId,
            alert.Category?.Value,
            alert.Detail,
            alert.Location?.Latitude,
            alert.Location?.Longitude,
            alert.AffectedStudents,
            alert.DriverUserId,
            alert.VehicleId,
            alert.OccurredAt,
            alert.AcknowledgedAt,
            alert.AcknowledgedBy?.Identifier,
            alert.ResolvedAt);

    public static TripRecordResource ToResourceFromEntity(TripRecord tripRecord) =>
        new(tripRecord.Id.Identifier,
            tripRecord.TripId.Identifier,
            tripRecord.RouteId.Identifier,
            tripRecord.RouteName,
            tripRecord.DeliveredStudents,
            tripRecord.AbsentStudents,
            tripRecord.IncidentCount,
            tripRecord.StartedAt,
            tripRecord.CompletedAt,
            tripRecord.Duration.TotalMinutes,
            tripRecord.AttendanceRate);
}
