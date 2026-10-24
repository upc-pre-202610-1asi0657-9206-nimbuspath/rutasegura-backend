using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Model.ValueObjects;
using RutaSegura.Monolith.Administration.Tests.Support;
using static RutaSegura.Monolith.Administration.Tests.Support.AdministrationMother;

namespace RutaSegura.Monolith.Administration.Tests.Domain;

public sealed class VehicleTests
{
    [Fact]
    public void Register_NewVehicle_IsEnabledForAssignments() =>
        NewVehicle().IsAssignable().ShouldBeTrue();

    [Fact]
    public void Disable_Vehicle_IsNoLongerAssignable()
    {
        var vehicle = NewVehicle();

        vehicle.Disable();

        vehicle.IsAssignable().ShouldBeFalse();
        Should.Throw<AdministrationDomainException>(vehicle.Disable)
            .Error.ShouldBe(AdministrationError.VehicleInvalidTransition);
    }

    [Fact]
    public void ChangeCapacity_BelowPlannedStudents_ThrowsCapacityExceeded()
    {
        var vehicle = NewVehicle(capacity: 15);

        Should.Throw<AdministrationDomainException>(() => vehicle.ChangeCapacity(new SeatingCapacity(9), 10))
            .Error.ShouldBe(AdministrationError.VehicleCapacityExceeded);
        vehicle.Capacity.Value.ShouldBe(15);
    }

    [Fact]
    public void ChangeCapacity_EnoughSeats_IsApplied()
    {
        var vehicle = NewVehicle(capacity: 15);

        vehicle.ChangeCapacity(new SeatingCapacity(10), 10);

        vehicle.Capacity.Value.ShouldBe(10);
    }
}
