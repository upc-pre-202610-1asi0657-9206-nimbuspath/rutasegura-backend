using System.Globalization;
using Monolith.Administration.Domain.Model.Events;
using Monolith.Shared.Domain.Model.Events;
// Microsoft.AspNetCore.Routing (implicit global using of the Web SDK) also defines Route.
using Route = Monolith.Administration.Domain.Model.Aggregates.Route;

namespace Monolith.Administration.Application.IntegrationEvents.Outbound;

/// <summary>
///     Translates the domain events of a Route into integration events (Anti-Corruption Layer towards other
///     services). The snapshot is taken from the aggregate after the change, so several changes made in the
///     same unit of work produce a single <c>route-updated</c>.
/// </summary>
public static class RouteIntegrationEventMapper
{
    public const int SchemaVersion = 1;

    /// <summary>True when the events produce a contract that carries the driver's IAM user id.</summary>
    public static bool RequiresDriverUserId(Route route, IEnumerable<IDomainEvent> domainEvents)
    {
        ArgumentNullException.ThrowIfNull(route);
        var events = domainEvents.ToList();
        return events.OfType<RouteAssignedEvent>().Any() ||
               events.OfType<RouteActivatedEvent>().Any() ||
               (route.IsActive() && events.OfType<RouteDetailsChangedEvent>().Any());
    }

    public static IReadOnlyList<IntegrationEvent> Map(Route route, IEnumerable<IDomainEvent> domainEvents,
        Guid? driverUserId, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(domainEvents);

        var events = domainEvents.ToList();
        var result = new List<IntegrationEvent>();

        foreach (var assigned in events.OfType<RouteAssignedEvent>())
            result.Add(new IntegrationEvent(assigned.EventId, AdministrationMessageTypes.RouteAssigned,
                SchemaVersion, assigned.AssignedAt, ToRouteAssigned(route, assigned, RequireDriver(driverUserId))));

        var activated = events.OfType<RouteActivatedEvent>().FirstOrDefault();
        var changed = events.OfType<RouteDetailsChangedEvent>().FirstOrDefault();
        if (activated is not null && route.IsActive())
            result.Add(new IntegrationEvent(activated.EventId, AdministrationMessageTypes.RoutePublished,
                SchemaVersion, occurredAt, ToSnapshot(route, RequireDriver(driverUserId))));
        else if (changed is not null && route.IsActive())
            result.Add(new IntegrationEvent(changed.EventId, AdministrationMessageTypes.RouteUpdated,
                SchemaVersion, occurredAt, ToSnapshot(route, RequireDriver(driverUserId))));

        var deactivated = events.OfType<RouteDeactivatedEvent>().FirstOrDefault();
        if (deactivated is not null)
            result.Add(new IntegrationEvent(deactivated.EventId, AdministrationMessageTypes.RouteRetired,
                SchemaVersion, occurredAt, new RouteRetiredV1(route.Id.Identifier)));

        return result;
    }

    /// <summary>Snapshot consumed by Trip &amp; Tracking (<c>RoutePublishedV1</c>).</summary>
    public static RoutePublishedV1 ToSnapshot(Route route, Guid driverUserId)
    {
        ArgumentNullException.ThrowIfNull(route);
        var assignment = route.Assignment
                         ?? throw new InvalidOperationException("Only assigned routes can be published.");
        var stops = route.Stops;

        return new RoutePublishedV1(
            route.Id.Identifier,
            route.OrganizationId.Identifier,
            route.Name.Value,
            ToContractDirection(route),
            driverUserId,
            assignment.VehicleId.Identifier,
            route.Schedule.DepartureTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            route.Schedule.TimeZoneId,
            ToContractDays(route),
            stops.Select(stop => new RouteStopV1(stop.Id.Identifier, stop.Sequence, stop.Name,
                stop.Location.Latitude, stop.Location.Longitude)).ToList(),
            stops.SelectMany(stop => stop.Students.Select(student =>
                new RouteStudentV1(student.Identifier, stop.Id.Identifier))).ToList());
    }

    private static RouteAssignedV1 ToRouteAssigned(Route route, RouteAssignedEvent assigned, Guid driverUserId) =>
        new(route.Id.Identifier,
            route.OrganizationId.Identifier,
            route.Name.Value,
            ToContractDirection(route),
            driverUserId,
            assigned.VehicleId.Identifier,
            route.Schedule.DepartureTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            route.Schedule.EstimatedArrivalTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            route.Schedule.TimeZoneId,
            ToContractDays(route),
            route.IsActive(),
            assigned.AssignedAt);

    /// <summary>Trip &amp; Tracking's <c>TripDirection</c> names: "Pickup" | "Return".</summary>
    private static string ToContractDirection(Route route) => route.Direction.IsPickup() ? "Pickup" : "Return";

    private static IReadOnlyList<string> ToContractDays(Route route) =>
        route.Schedule.OperatingDays.Select(day => day.ToString()).ToList();

    private static Guid RequireDriver(Guid? driverUserId) =>
        driverUserId is { } id && id != Guid.Empty
            ? id
            : throw new InvalidOperationException("The IAM user id of the assigned driver is required.");
}
