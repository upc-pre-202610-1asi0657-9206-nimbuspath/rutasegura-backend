using Monolith.Administration.Domain.Model.Entities;
using Monolith.Administration.Domain.Model.Events;
using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.Events;
using Monolith.Shared.Domain.Model.ValueObjects;
// Microsoft.AspNetCore.Routing (implicit global using of the Web SDK) also defines RouteDirection.
using RouteDirection = Monolith.Administration.Domain.Model.ValueObjects.RouteDirection;

namespace Monolith.Administration.Domain.Model.Aggregates;

/// <summary>
///     Route Planning aggregate: a school route with its schedule, ordered stops, the students of each stop and
///     the driver/vehicle assignment (US-04, US-05).
/// </summary>
/// <remarks>
///     Invariants guarded here:
///     <list type="bullet">
///         <item>Stops keep a contiguous 1..n sequence and are at least 60 m apart (Trip &amp; Tracking's arrival radius).</item>
///         <item>A student belongs to a single stop of the route.</item>
///         <item>Planned students never exceed the seating capacity of the assigned vehicle (aforo).</item>
///         <item>Only verified drivers with a valid licence and enabled vehicles of the same organization are assigned.</item>
///         <item>An ACTIVE route always has at least one stop and an assignment, and activating one respects the plan limit.</item>
///     </list>
///     Rules that need other routes (schedule conflicts) live in <c>RouteScheduleConflictPolicy</c>.
/// </remarks>
public partial class Route : IHasDomainEvents
{
    public const int MaxStops = 30;
    public const double MinimumDistanceBetweenStopsMeters = 60;

    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly List<RouteStop> _stops = [];

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected Route()
    {
        Id = new RouteId();
        OrganizationId = new OrganizationId();
        Name = new RouteName();
        Direction = new RouteDirection();
        Schedule = new RouteSchedule();
        Status = RouteStatus.CreateDraft();
        EnrollmentCode = new RouteEnrollmentCode();
    }

    /// <summary>Creates a route in DRAFT, without stops or assignment.</summary>
    public Route(OrganizationId organizationId, RouteName name, RouteDirection direction, RouteSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        if (organizationId.Identifier == Guid.Empty)
            throw new ArgumentException("The route must belong to an organization.", nameof(organizationId));

        Id = RouteId.New();
        OrganizationId = organizationId;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Direction = direction ?? throw new ArgumentNullException(nameof(direction));
        Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        Status = RouteStatus.CreateDraft();
        EnrollmentCode = RouteEnrollmentCode.Generate();
        _domainEvents.Add(new RouteCreatedEvent(Id, OrganizationId));
    }

    public RouteId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public RouteName Name { get; private set; }

    public RouteDirection Direction { get; private set; }

    public RouteSchedule Schedule { get; private set; }

    public RouteStatus Status { get; private set; }

    public RouteAssignment? Assignment { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    /// <summary>Code families use to enroll their children (see <see cref="PlaceStudent" />).</summary>
    public RouteEnrollmentCode EnrollmentCode { get; private set; }

    /// <summary>Stops ordered by sequence.</summary>
    public IReadOnlyList<RouteStop> Stops => _stops.OrderBy(stop => stop.Sequence).ToList();

    public int StudentCount => _stops.Sum(stop => stop.Students.Count);

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public bool IsActive() => Status.IsActive();

    public bool IsAssigned() => Assignment is not null;

    public bool UsesDriver(DriverId driverId) => Assignment is not null && Assignment.DriverId == driverId;

    public bool UsesVehicle(VehicleId vehicleId) => Assignment is not null && Assignment.VehicleId == vehicleId;

    public bool HasStudent(StudentId studentId) => _stops.Any(stop => stop.HasStudent(studentId));

    /// <summary>
    ///     DRAFT and ACTIVE routes reserve their driver and vehicle in their time window; INACTIVE routes release them.
    /// </summary>
    public bool ReservesResources() => !Status.IsInactive() && Assignment is not null;

    public RouteStop? FindStop(RouteStopId stopId) => _stops.FirstOrDefault(stop => stop.Id == stopId);

    // ── Details ──────────────────────────────────────────────────────────────

    public void Rename(RouteName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name == Name) return;
        Name = name;
        RecordDetailsChanged();
    }

    /// <summary>Changes the schedule. Conflicts with other routes are validated beforehand by the policy.</summary>
    public void Reschedule(RouteSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule == Schedule) return;
        Schedule = schedule;
        RecordDetailsChanged();
    }

    /// <summary>The direction defines what the stops mean, so it can only change before the route operates.</summary>
    public void ChangeDirection(RouteDirection direction)
    {
        ArgumentNullException.ThrowIfNull(direction);
        if (IsActive())
            throw new AdministrationDomainException(AdministrationError.RouteInvalidTransition,
                "The direction of an active route cannot change; deactivate it first.");
        Direction = direction;
    }

    // ── Stops ────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Adds a stop at <paramref name="position" /> (1-based) or at the end when it is null. Following stops
    ///     are shifted.
    /// </summary>
    public RouteStop AddStop(string name, string? address, GeoLocation location, int? position = null) =>
        InsertStop(name, address, location, position, StopOrigin.CreateManual());

    private RouteStop InsertStop(string name, string? address, GeoLocation location, int? position, StopOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (_stops.Count >= MaxStops)
            throw new AdministrationDomainException(AdministrationError.RouteStopLimitReached,
                $"A route cannot have more than {MaxStops} stops.");
        if (position is < 1)
            throw new ArgumentException("Stop position must be 1 or greater.", nameof(position));
        EnsureLocationIsFree(location, null);

        var ordered = Stops.ToList();
        var index = position is null ? ordered.Count : Math.Min(position.Value - 1, ordered.Count);
        var stop = new RouteStop(index + 1, name, address, location, origin);
        ordered.Insert(index, stop);
        _stops.Add(stop);
        Resequence(ordered);
        RecordDetailsChanged();
        return stop;
    }

    public void UpdateStop(RouteStopId stopId, string name, string? address, GeoLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var stop = GetStop(stopId);
        EnsureLocationIsFree(location, stopId);
        stop.Update(name, address, location);
        RecordDetailsChanged();
    }

    /// <summary>Removes an empty stop. Students must be moved or unassigned first.</summary>
    public void RemoveStop(RouteStopId stopId)
    {
        var stop = GetStop(stopId);
        if (stop.Students.Count > 0)
            throw new AdministrationDomainException(AdministrationError.RouteStopHasStudents,
                $"Stop '{stop.Name}' still has {stop.Students.Count} students; move or unassign them first.");
        if (IsActive() && _stops.Count == 1)
            throw new AdministrationDomainException(AdministrationError.ActiveRouteRequiresStops,
                "An active route must keep at least one stop.");

        _stops.Remove(stop);
        Resequence(Stops.ToList());
        RecordDetailsChanged();
    }

    /// <summary>Sets a new order. <paramref name="orderedStopIds" /> must contain every stop exactly once.</summary>
    public void ReorderStops(IReadOnlyList<RouteStopId> orderedStopIds)
    {
        ArgumentNullException.ThrowIfNull(orderedStopIds);
        if (orderedStopIds.Count != _stops.Count || orderedStopIds.Distinct().Count() != _stops.Count ||
            orderedStopIds.Any(id => FindStop(id) is null))
            throw new ArgumentException("The new order must include every stop of the route exactly once.",
                nameof(orderedStopIds));

        Resequence(orderedStopIds.Select(GetStop).ToList());
        RecordDetailsChanged();
    }

    // ── Students ─────────────────────────────────────────────────────────────

    /// <summary>
    ///     Sets the stop where a student is picked up / dropped off. A student already on another stop of this
    ///     route is moved. <paramref name="seatingCapacity" /> is the capacity of the assigned vehicle, if any.
    /// </summary>
    public void AssignStudentToStop(RouteStopId stopId, StudentId studentId, SeatingCapacity? seatingCapacity)
    {
        ArgumentNullException.ThrowIfNull(studentId);
        var target = GetStop(stopId);
        var current = _stops.FirstOrDefault(stop => stop.HasStudent(studentId));
        if (ReferenceEquals(current, target)) return;

        if (current is null && seatingCapacity is not null && !seatingCapacity.CanSeat(StudentCount + 1))
            throw new AdministrationDomainException(AdministrationError.VehicleCapacityExceeded,
                $"The assigned vehicle only has {seatingCapacity.Value} seats.");

        current?.RemoveStudent(studentId);
        target.AddStudent(studentId);
        if (current is not null) RemoveIfDisposable(current);
        RecordDetailsChanged();
    }

    public void UnassignStudent(StudentId studentId)
    {
        ArgumentNullException.ThrowIfNull(studentId);
        var stop = _stops.FirstOrDefault(s => s.HasStudent(studentId))
                   ?? throw new AdministrationDomainException(AdministrationError.StudentNotOnRoute,
                       "The student is not planned on this route.");
        stop.RemoveStudent(studentId);
        RemoveIfDisposable(stop);
        RecordDetailsChanged();
    }

    // ── Assignment (US-05) ───────────────────────────────────────────────────

    /// <summary>
    ///     Assigns the driver and vehicle that operate the route. Schedule conflicts with other routes must be
    ///     checked beforehand with <c>RouteScheduleConflictPolicy</c>.
    /// </summary>
    public void AssignResources(Driver driver, Vehicle vehicle, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(vehicle);
        EnsureResourcesCanServe(driver, vehicle, now);

        var previous = Assignment;
        if (previous is not null && previous.DriverId == driver.Id && previous.VehicleId == vehicle.Id) return;

        Assignment = new RouteAssignment(driver.Id, vehicle.Id, now);
        _domainEvents.Add(new RouteAssignedEvent(Id, OrganizationId, driver.Id, vehicle.Id,
            previous?.DriverId, previous?.VehicleId, now));
        RecordDetailsChanged();
    }

    /// <summary>Removes driver and vehicle from a route that is not operating.</summary>
    public void ReleaseResources()
    {
        if (IsActive())
            throw new AdministrationDomainException(AdministrationError.RouteInvalidTransition,
                "An active route must keep its driver and vehicle; assign others or deactivate it first.");
        Assignment = null;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>
    ///     Puts the route into operation. Requires stops and an assignment whose driver and vehicle can still
    ///     serve, and a plan that allows one more active route.
    /// </summary>
    /// <param name="driver">Driver currently assigned.</param>
    /// <param name="vehicle">Vehicle currently assigned.</param>
    /// <param name="activeRoutesInOrganization">ACTIVE routes the organization already has.</param>
    /// <param name="limits">Limits of the contracted plan (Subscription).</param>
    /// <param name="now">Current instant.</param>
    public void Activate(Driver driver, Vehicle vehicle, int activeRoutesInOrganization, PlanLimits limits,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(limits);

        if (IsActive())
            throw new AdministrationDomainException(AdministrationError.RouteInvalidTransition,
                "The route is already active.");
        if (_stops.Count == 0)
            throw new AdministrationDomainException(AdministrationError.RouteNotReadyForActivation,
                "The route needs at least one stop before it can be activated.");
        if (Assignment is null)
            throw new AdministrationDomainException(AdministrationError.RouteNotReadyForActivation,
                "The route needs a driver and a vehicle before it can be activated.");
        if (Assignment.DriverId != driver.Id || Assignment.VehicleId != vehicle.Id)
            throw new ArgumentException("The driver and vehicle must be the ones assigned to the route.");

        EnsureResourcesCanServe(driver, vehicle, now);

        if (!limits.AllowsAnotherActiveRoute(activeRoutesInOrganization))
            throw new AdministrationDomainException(AdministrationError.PlanRouteLimitReached,
                $"Plan {limits.PlanCode} allows {limits.MaxActiveRoutes} active routes and the operation " +
                $"already has {activeRoutesInOrganization}.");

        Status = RouteStatus.CreateActive();
        ActivatedAt = now;
        _domainEvents.Add(new RouteActivatedEvent(Id, OrganizationId));
    }

    /// <summary>Stops the operation of the route; it no longer counts against the plan limit.</summary>
    public void Deactivate()
    {
        if (!IsActive())
            throw new AdministrationDomainException(AdministrationError.RouteInvalidTransition,
                "Only active routes can be deactivated.");
        Status = RouteStatus.CreateInactive();
        _domainEvents.RemoveAll(e => e is RouteDetailsChangedEvent);
        _domainEvents.Add(new RouteDeactivatedEvent(Id, OrganizationId));
    }

    /// <summary>Active routes are being operated by Trip &amp; Tracking and cannot be deleted.</summary>
    public void EnsureCanBeDeleted()
    {
        if (IsActive())
            throw new AdministrationDomainException(AdministrationError.RouteInvalidTransition,
                "An active route cannot be deleted; deactivate it first.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void EnsureResourcesCanServe(Driver driver, Vehicle vehicle, DateTimeOffset now)
    {
        if (driver.OrganizationId != OrganizationId || vehicle.OrganizationId != OrganizationId)
            throw new AdministrationDomainException(AdministrationError.ResourceBelongsToAnotherOrganization,
                "The driver and the vehicle must belong to the same organization as the route.");
        if (!driver.IsAssignableOn(Schedule.LocalDateOf(now)))
            throw new AdministrationDomainException(AdministrationError.DriverNotAssignable,
                $"Driver {driver.GetFullName()} is not verified or the driver license is expired.");
        if (!vehicle.IsAssignable())
            throw new AdministrationDomainException(AdministrationError.VehicleNotAssignable,
                $"Vehicle {vehicle.LicensePlate} is disabled.");
        if (!vehicle.Capacity.CanSeat(StudentCount))
            throw new AdministrationDomainException(AdministrationError.VehicleCapacityExceeded,
                $"Vehicle {vehicle.LicensePlate} has {vehicle.Capacity.Value} seats and the route has " +
                $"{StudentCount} students.");
    }

    private RouteStop GetStop(RouteStopId stopId) =>
        FindStop(stopId) ?? throw new AdministrationDomainException(AdministrationError.RouteStopNotFound,
            "The stop does not belong to this route.");

    private void EnsureLocationIsFree(GeoLocation location, RouteStopId? ignoredStop)
    {
        var tooClose = _stops.FirstOrDefault(stop =>
            stop.Id != ignoredStop &&
            stop.Location.DistanceInMetersTo(location) < MinimumDistanceBetweenStopsMeters);
        if (tooClose is not null)
            throw new AdministrationDomainException(AdministrationError.RouteStopTooClose,
                $"The location is less than {MinimumDistanceBetweenStopsMeters} m away from stop '{tooClose.Name}'.");
    }

    private static void Resequence(IReadOnlyList<RouteStop> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].ChangeSequence(i + 1);
    }

    /// <summary>Only an ACTIVE route has to tell Trip &amp; Tracking about changes; one event per unit of work.</summary>
    private void RecordDetailsChanged()
    {
        if (IsActive() && !_domainEvents.Any(e => e is RouteDetailsChangedEvent))
            _domainEvents.Add(new RouteDetailsChangedEvent(Id, OrganizationId));
    }
}
