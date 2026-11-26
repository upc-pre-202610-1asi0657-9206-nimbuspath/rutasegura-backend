using Monolith.Administration.Domain.Model.Entities;
using Monolith.Administration.Domain.Model.ValueObjects;

namespace Monolith.Administration.Domain.Model.Aggregates;

/// <summary>
///     The destination (the school) is a stop of the route that stays at one end: last on a PICKUP route, first on a
///     RETURN route. Families see it before enrolling; their pickup stops are added around it.
/// </summary>
public partial class Route
{
    public RouteStop? DestinationStop() => _stops.FirstOrDefault(stop => stop.Origin.IsDestination());

    /// <summary>Creates the destination of the route or changes the existing one.</summary>
    public void SetDestination(string name, string? address, GeoLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (DestinationStop() is { } current)
            UpdateStop(current.Id, name, address, location);
        else
            InsertStop(name, address, location, null, StopOrigin.CreateDestination());
    }

    private IReadOnlyList<RouteStop> PinDestination(IReadOnlyList<RouteStop> ordered)
    {
        var destination = ordered.FirstOrDefault(stop => stop.Origin.IsDestination());
        if (destination is null) return ordered;
        var others = ordered.Where(stop => !ReferenceEquals(stop, destination));
        return Direction.IsPickup()
            ? others.Append(destination).ToList()
            : others.Prepend(destination).ToList();
    }
}
