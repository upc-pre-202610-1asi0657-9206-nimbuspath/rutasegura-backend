using Monolith.Administration.Domain.Model.Entities;
using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Model.ValueObjects;

namespace Monolith.Administration.Domain.Model.Aggregates;

/// <summary>
///     Route planning driven by the families: when a parent enrolls a child (Stakeholder bounded context), the
///     route places the child at the stop that matches the pickup address, creating the stop when there is none
///     nearby, and cleans it up when the child leaves.
/// </summary>
public partial class Route
{
    /// <summary>A pickup address closer than this to an existing stop uses that stop (walking distance).</summary>
    public const double JoinStopRadiusMeters = 150;

    /// <summary>Routes being retired (INACTIVE) do not accept new children.</summary>
    public bool AcceptsEnrollments() => !Status.IsInactive();

    /// <summary>Gives the route a new enrollment code; the previous one stops working.</summary>
    public void RegenerateEnrollmentCode() => EnrollmentCode = RouteEnrollmentCode.Generate();

    /// <summary>
    ///     Places (or re-places, after the pickup address changed) a student at the stop of its pickup address:
    ///     <list type="number">
    ///         <item>The nearest stop within <see cref="JoinStopRadiusMeters" /> is used when there is one.</item>
    ///         <item>Otherwise an ENROLLMENT stop is created at the address, inserted where it lengthens the route the least.</item>
    ///         <item>An ENROLLMENT stop left without students is removed.</item>
    ///     </list>
    ///     The vehicle capacity and the stop limit of the route are respected.
    /// </summary>
    /// <returns>The stop where the student is picked up / dropped off.</returns>
    public RouteStop PlaceStudent(StudentId studentId, GeoLocation pickupLocation, string? pickupAddress,
        SeatingCapacity? seatingCapacity)
    {
        ArgumentNullException.ThrowIfNull(studentId);
        ArgumentNullException.ThrowIfNull(pickupLocation);
        if (!AcceptsEnrollments())
            throw new AdministrationDomainException(AdministrationError.RouteNotAcceptingEnrollments,
                "The route is inactive and does not accept new students.");

        var current = _stops.FirstOrDefault(stop => stop.HasStudent(studentId));
        // Children are not picked up at the destination, unless their address is too close to it for another stop.
        var nearest = _stops
            .Select(stop => (Stop: stop, Distance: stop.Location.DistanceInMetersTo(pickupLocation)))
            .Where(candidate => candidate.Distance <= (candidate.Stop.Origin.IsDestination()
                ? MinimumDistanceBetweenStopsMeters
                : JoinStopRadiusMeters))
            .OrderBy(candidate => candidate.Stop.Origin.IsDestination())
            .ThenBy(candidate => candidate.Distance)
            .Select(candidate => candidate.Stop)
            .FirstOrDefault();

        if (nearest is not null && ReferenceEquals(nearest, current)) return current;

        if (current is null && seatingCapacity is not null && !seatingCapacity.CanSeat(StudentCount + 1))
            throw new AdministrationDomainException(AdministrationError.VehicleCapacityExceeded,
                $"The route is full: the assigned vehicle only has {seatingCapacity.Value} seats.");

        var currentWillDisappear = current is not null && current.Origin.IsEnrollment() &&
                                   current.Students.Count == 1;
        if (nearest is null && _stops.Count - (currentWillDisappear ? 1 : 0) >= MaxStops)
            throw new AdministrationDomainException(AdministrationError.RouteStopLimitReached,
                $"The route already has {MaxStops} stops; the administrator must merge stops first.");

        if (current is not null)
        {
            current.RemoveStudent(studentId);
            RemoveIfDisposable(current);
        }

        var target = nearest ?? InsertStop(StopNameFor(pickupAddress), pickupAddress, pickupLocation,
            CheapestInsertionPosition(pickupLocation), StopOrigin.CreateEnrollment());
        target.AddStudent(studentId);
        RecordDetailsChanged();
        return target;
    }

    /// <summary>
    ///     Takes a student out of the route (the parent withdrew the enrollment). Unlike <see cref="UnassignStudent" />
    ///     it is idempotent: a student that is no longer planned is not an error.
    /// </summary>
    /// <returns>True when the student was on the route.</returns>
    public bool RemoveStudent(StudentId studentId)
    {
        ArgumentNullException.ThrowIfNull(studentId);
        var stop = _stops.FirstOrDefault(s => s.HasStudent(studentId));
        if (stop is null) return false;
        stop.RemoveStudent(studentId);
        RemoveIfDisposable(stop);
        RecordDetailsChanged();
        return true;
    }

    /// <summary>The stop of the student on this route, if planned.</summary>
    public RouteStop? StopOf(StudentId studentId) => _stops.FirstOrDefault(stop => stop.HasStudent(studentId));

    /// <summary>
    ///     1-based position that adds the fewest meters to the route (cheapest insertion), without going past the
    ///     destination. The administrator can always reorder the stops afterwards.
    /// </summary>
    private int CheapestInsertionPosition(GeoLocation location)
    {
        var ordered = Stops;
        if (ordered.Count == 0) return 1;

        var first = ordered[0].Origin.IsDestination() && !Direction.IsPickup() ? 2 : 1;
        var last = ordered[^1].Origin.IsDestination() && Direction.IsPickup() ? ordered.Count : ordered.Count + 1;

        double Cost(int position)
        {
            if (position == 1) return location.DistanceInMetersTo(ordered[0].Location);
            if (position > ordered.Count) return ordered[^1].Location.DistanceInMetersTo(location);
            var previous = ordered[position - 2].Location;
            var next = ordered[position - 1].Location;
            return previous.DistanceInMetersTo(location) + location.DistanceInMetersTo(next) -
                   previous.DistanceInMetersTo(next);
        }

        // Ties favour the end of the route, then its start.
        return new[] { ordered.Count + 1, 1 }
            .Concat(Enumerable.Range(2, ordered.Count - 1))
            .Where(position => position >= first && position <= last)
            .MinBy(Cost);
    }

    /// <summary>An automatic stop is removed with its last student (an active route keeps at least one stop).</summary>
    private void RemoveIfDisposable(RouteStop stop)
    {
        if (!stop.IsDisposable || !_stops.Contains(stop)) return;
        if (IsActive() && _stops.Count == 1) return;
        _stops.Remove(stop);
        InvalidateItinerary();
        Resequence(Stops.ToList());
    }

    private string StopNameFor(string? address)
    {
        var trimmed = (address ?? string.Empty).Trim();
        if (trimmed.Length == 0) return $"Parada {_stops.Count + 1}";
        return trimmed.Length <= RouteStop.NameMaxLength ? trimmed : trimmed[..RouteStop.NameMaxLength];
    }
}
