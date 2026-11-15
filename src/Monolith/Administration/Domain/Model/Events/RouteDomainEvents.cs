using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.Events;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Administration.Domain.Model.Events;

// Domain events recorded by the Route aggregate. The application layer translates them into the
// integration events Trip & Tracking listens to (administration.route-published / -updated / -retired)
// plus administration.route-assigned (RouteAssignedEvent of the architecture report).

/// <summary>Base type with a unique identifier per event.</summary>
public abstract record AdministrationDomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}

/// <summary>A route was created in DRAFT.</summary>
public sealed record RouteCreatedEvent(RouteId RouteId, OrganizationId OrganizationId) : AdministrationDomainEvent;

/// <summary>
///     A driver and a vehicle were assigned to a route (US-05). Raised on the first assignment and on every
///     re-assignment; not raised when the same pair is assigned again.
/// </summary>
public sealed record RouteAssignedEvent(
    RouteId RouteId,
    OrganizationId OrganizationId,
    DriverId DriverId,
    VehicleId VehicleId,
    DriverId? PreviousDriverId,
    VehicleId? PreviousVehicleId,
    DateTimeOffset AssignedAt) : AdministrationDomainEvent;

/// <summary>The route started operating; Trip &amp; Tracking may now schedule trips for it.</summary>
public sealed record RouteActivatedEvent(RouteId RouteId, OrganizationId OrganizationId) : AdministrationDomainEvent;

/// <summary>
///     Something Trip &amp; Tracking needs (name, schedule, stops, students or assignment) changed while the route
///     was ACTIVE. Recorded at most once per unit of work.
/// </summary>
public sealed record RouteDetailsChangedEvent(RouteId RouteId, OrganizationId OrganizationId) : AdministrationDomainEvent;

/// <summary>The route stopped operating; no more trips must be scheduled for it.</summary>
public sealed record RouteDeactivatedEvent(RouteId RouteId, OrganizationId OrganizationId) : AdministrationDomainEvent;
