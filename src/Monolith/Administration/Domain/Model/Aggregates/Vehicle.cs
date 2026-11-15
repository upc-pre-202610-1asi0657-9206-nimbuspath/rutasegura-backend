using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Administration.Domain.Model.Aggregates;

/// <summary>
///     Roster aggregate: a vehicle of the operation's fleet and its seating capacity (US-08).
/// </summary>
public partial class Vehicle
{
    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected Vehicle()
    {
        Id = new VehicleId();
        OrganizationId = new OrganizationId();
        LicensePlate = new LicensePlate();
        Capacity = new SeatingCapacity();
        Model = new VehicleModel();
        Status = VehicleStatus.CreateEnabled();
    }

    /// <summary>Registers a vehicle; it is enabled for assignments right away.</summary>
    public Vehicle(OrganizationId organizationId, LicensePlate licensePlate, SeatingCapacity capacity,
        VehicleModel model)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        if (organizationId.Identifier == Guid.Empty)
            throw new ArgumentException("The vehicle must belong to an organization.", nameof(organizationId));

        Id = VehicleId.New();
        OrganizationId = organizationId;
        LicensePlate = licensePlate ?? throw new ArgumentNullException(nameof(licensePlate));
        Capacity = capacity ?? throw new ArgumentNullException(nameof(capacity));
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Status = VehicleStatus.CreateEnabled();
    }

    public VehicleId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public LicensePlate LicensePlate { get; private set; }

    public SeatingCapacity Capacity { get; private set; }

    public VehicleModel Model { get; private set; }

    public VehicleStatus Status { get; private set; }

    public void Enable()
    {
        if (Status.IsEnabled())
            throw new AdministrationDomainException(AdministrationError.VehicleInvalidTransition,
                "The vehicle is already enabled.");
        Status = VehicleStatus.CreateEnabled();
    }

    /// <summary>Takes the vehicle out of service. The application layer checks that no active route uses it.</summary>
    public void Disable()
    {
        if (Status.IsDisabled())
            throw new AdministrationDomainException(AdministrationError.VehicleInvalidTransition,
                "The vehicle is already disabled.");
        Status = VehicleStatus.CreateDisabled();
    }

    /// <summary>
    ///     Changes the seating capacity. It cannot drop below the students already planned on the routes that use
    ///     this vehicle (aforo).
    /// </summary>
    public void ChangeCapacity(SeatingCapacity capacity, int studentsOnAssignedRoutes)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        if (!capacity.CanSeat(studentsOnAssignedRoutes))
            throw new AdministrationDomainException(AdministrationError.VehicleCapacityExceeded,
                $"A route using this vehicle already has {studentsOnAssignedRoutes} students; " +
                $"the capacity cannot be reduced to {capacity.Value}.");
        Capacity = capacity;
    }

    public void UpdateModel(VehicleModel model) => Model = model ?? throw new ArgumentNullException(nameof(model));

    public bool IsAssignable() => Status.IsEnabled();
}
