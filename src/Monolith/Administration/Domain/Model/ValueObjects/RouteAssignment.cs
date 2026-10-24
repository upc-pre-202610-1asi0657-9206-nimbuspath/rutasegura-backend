namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     Driver and vehicle in charge of a route (US-05). Kept as references by identity: the route never
///     holds the Driver or Vehicle aggregates themselves.
/// </summary>
public record RouteAssignment
{
    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    public RouteAssignment()
    {
        DriverId = new DriverId();
        VehicleId = new VehicleId();
    }

    public RouteAssignment(DriverId driverId, VehicleId vehicleId, DateTimeOffset assignedAt)
    {
        DriverId = driverId ?? throw new ArgumentNullException(nameof(driverId));
        VehicleId = vehicleId ?? throw new ArgumentNullException(nameof(vehicleId));
        AssignedAt = assignedAt;
    }

    public DriverId DriverId { get; init; }

    public VehicleId VehicleId { get; init; }

    public DateTimeOffset AssignedAt { get; init; }
}
