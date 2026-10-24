using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Events;
using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Model.ValueObjects;
using RutaSegura.Monolith.Administration.Tests.Support;
using static RutaSegura.Monolith.Administration.Tests.Support.AdministrationMother;

namespace RutaSegura.Monolith.Administration.Tests.Domain;

/// <summary>Driver/vehicle assignment (US-05) and route lifecycle with the plan limit.</summary>
public sealed class RouteAssignmentAndLifecycleTests
{
    // ── Assignment ───────────────────────────────────────────────────────────

    [Fact]
    public void AssignResources_VerifiedDriverAndEnabledVehicle_RecordsAssignmentAndRaisesRouteAssigned()
    {
        var route = NewRoute();
        var driver = NewDriver();
        var vehicle = NewVehicle();
        route.ClearDomainEvents();

        route.AssignResources(driver, vehicle, Now);

        route.Assignment.ShouldNotBeNull();
        route.Assignment.DriverId.ShouldBe(driver.Id);
        route.Assignment.VehicleId.ShouldBe(vehicle.Id);
        route.Assignment.AssignedAt.ShouldBe(Now);
        var assigned = route.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<RouteAssignedEvent>();
        assigned.PreviousDriverId.ShouldBeNull();
    }

    [Fact]
    public void AssignResources_SamePairAgain_DoesNotRaiseAnotherEvent()
    {
        var driver = NewDriver();
        var vehicle = NewVehicle();
        var route = ReadyRoute(driver, vehicle);

        route.AssignResources(driver, vehicle, Now.AddHours(1));

        route.DomainEvents.ShouldBeEmpty();
        route.Assignment!.AssignedAt.ShouldBe(Now);
    }

    [Fact]
    public void AssignResources_AnotherDriver_RaisesRouteAssignedWithThePreviousOne()
    {
        var first = NewDriver();
        var vehicle = NewVehicle();
        var route = ReadyRoute(first, vehicle);
        var second = NewDriver();

        route.AssignResources(second, vehicle, Now);

        var assigned = route.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<RouteAssignedEvent>();
        assigned.DriverId.ShouldBe(second.Id);
        assigned.PreviousDriverId.ShouldBe(first.Id);
    }

    [Fact]
    public void AssignResources_DriverPendingVerification_ThrowsDriverNotAssignable()
    {
        var route = NewRoute();

        Should.Throw<AdministrationDomainException>(() =>
                route.AssignResources(NewDriver(verified: false), NewVehicle(), Now))
            .Error.ShouldBe(AdministrationError.DriverNotAssignable);
        route.Assignment.ShouldBeNull();
    }

    [Fact]
    public void AssignResources_DriverWithExpiredLicense_ThrowsDriverNotAssignable()
    {
        var driver = NewDriver(licenseExpiresOn: Today);
        var route = NewRoute();

        Should.Throw<AdministrationDomainException>(() =>
                route.AssignResources(driver, NewVehicle(), Now.AddDays(1)))
            .Error.ShouldBe(AdministrationError.DriverNotAssignable);
    }

    [Fact]
    public void AssignResources_DisabledVehicle_ThrowsVehicleNotAssignable()
    {
        var vehicle = NewVehicle();
        vehicle.Disable();

        Should.Throw<AdministrationDomainException>(() =>
                NewRoute().AssignResources(NewDriver(), vehicle, Now))
            .Error.ShouldBe(AdministrationError.VehicleNotAssignable);
    }

    [Fact]
    public void AssignResources_VehicleWithFewerSeatsThanStudents_ThrowsCapacityExceeded()
    {
        var route = NewRoute();
        var stop = route.AddStop("Parada 1", null, StopLocation(0));
        for (var i = 0; i < 5; i++) route.AssignStudentToStop(stop.Id, new StudentId(Guid.NewGuid()), null);

        Should.Throw<AdministrationDomainException>(() =>
                route.AssignResources(NewDriver(), NewVehicle(capacity: 4), Now))
            .Error.ShouldBe(AdministrationError.VehicleCapacityExceeded);
    }

    [Fact]
    public void AssignResources_ResourcesOfAnotherOrganization_Throws()
    {
        var foreignDriver = NewDriver(OtherOrganization);

        Should.Throw<AdministrationDomainException>(() =>
                NewRoute().AssignResources(foreignDriver, NewVehicle(), Now))
            .Error.ShouldBe(AdministrationError.ResourceBelongsToAnotherOrganization);
    }

    // ── Activation and plan limit ────────────────────────────────────────────

    [Fact]
    public void Activate_WithoutStops_ThrowsRouteNotReadyForActivation()
    {
        var route = NewRoute();

        Should.Throw<AdministrationDomainException>(() =>
                route.Activate(NewDriver(), NewVehicle(), 0, Plan(), Now))
            .Error.ShouldBe(AdministrationError.RouteNotReadyForActivation);
    }

    [Fact]
    public void Activate_WithoutAssignment_ThrowsRouteNotReadyForActivation()
    {
        var route = NewRoute();
        route.AddStop("Parada 1", null, StopLocation(0));

        Should.Throw<AdministrationDomainException>(() =>
                route.Activate(NewDriver(), NewVehicle(), 0, Plan(), Now))
            .Error.ShouldBe(AdministrationError.RouteNotReadyForActivation);
    }

    [Fact]
    public void Activate_PlanLimitReached_ThrowsPlanRouteLimitReachedAndStaysDraft()
    {
        var driver = NewDriver();
        var vehicle = NewVehicle();
        var route = ReadyRoute(driver, vehicle);

        Should.Throw<AdministrationDomainException>(() =>
                route.Activate(driver, vehicle, activeRoutesInOrganization: 2, Plan(maxActiveRoutes: 2), Now))
            .Error.ShouldBe(AdministrationError.PlanRouteLimitReached);
        route.Status.IsDraft().ShouldBeTrue();
        route.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Activate_ReadyRouteWithinPlan_IsActiveAndRaisesRouteActivated()
    {
        var driver = NewDriver();
        var vehicle = NewVehicle();
        var route = ReadyRoute(driver, vehicle);

        route.Activate(driver, vehicle, activeRoutesInOrganization: 1, Plan(maxActiveRoutes: 2), Now);

        route.IsActive().ShouldBeTrue();
        route.ActivatedAt.ShouldBe(Now);
        route.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<RouteActivatedEvent>();
    }

    [Fact]
    public void Activate_DriverSuspendedAfterAssignment_ThrowsDriverNotAssignable()
    {
        var driver = NewDriver();
        var vehicle = NewVehicle();
        var route = ReadyRoute(driver, vehicle);
        driver.Suspend();

        Should.Throw<AdministrationDomainException>(() => route.Activate(driver, vehicle, 0, Plan(), Now))
            .Error.ShouldBe(AdministrationError.DriverNotAssignable);
    }

    [Fact]
    public void Changes_OnActiveRoute_RaiseASingleRouteDetailsChanged()
    {
        var route = ActiveRoute();

        route.Rename(new RouteName("Ruta 3 - Mañana"));
        route.AddStop("Nueva parada", null, StopLocation(5));
        route.AssignStudentToStop(route.Stops[0].Id, new StudentId(Guid.NewGuid()), new SeatingCapacity(15));

        route.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<RouteDetailsChangedEvent>();
    }

    [Fact]
    public void Changes_OnDraftRoute_DoNotRaiseRouteDetailsChanged()
    {
        var route = ReadyRoute(NewDriver(), NewVehicle());

        route.Rename(new RouteName("Ruta 3 - Mañana"));
        route.AddStop("Nueva parada", null, StopLocation(5));

        route.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void RemoveStop_LastStopOfActiveRoute_ThrowsActiveRouteRequiresStops()
    {
        var driver = NewDriver();
        var vehicle = NewVehicle();
        var route = NewRoute();
        var only = route.AddStop("Única parada", null, StopLocation(0));
        route.AssignResources(driver, vehicle, Now);
        route.Activate(driver, vehicle, 0, Plan(), Now);

        Should.Throw<AdministrationDomainException>(() => route.RemoveStop(only.Id))
            .Error.ShouldBe(AdministrationError.ActiveRouteRequiresStops);
    }

    [Fact]
    public void Deactivate_ActiveRoute_IsInactiveAndRaisesOnlyRouteDeactivated()
    {
        var route = ActiveRoute();
        route.Rename(new RouteName("Ruta 3 - Antes de retirarla"));

        route.Deactivate();

        route.Status.IsInactive().ShouldBeTrue();
        route.ReservesResources().ShouldBeFalse();
        route.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<RouteDeactivatedEvent>();
    }

    [Fact]
    public void Deactivate_DraftRoute_ThrowsInvalidTransition() =>
        Should.Throw<AdministrationDomainException>(() => NewRoute().Deactivate())
            .Error.ShouldBe(AdministrationError.RouteInvalidTransition);

    [Fact]
    public void ActiveRoute_CannotLoseItsResourcesChangeDirectionOrBeDeleted()
    {
        var route = ActiveRoute();

        Should.Throw<AdministrationDomainException>(route.ReleaseResources)
            .Error.ShouldBe(AdministrationError.RouteInvalidTransition);
        Should.Throw<AdministrationDomainException>(() => route.ChangeDirection(RouteDirection.CreateReturn()))
            .Error.ShouldBe(AdministrationError.RouteInvalidTransition);
        Should.Throw<AdministrationDomainException>(route.EnsureCanBeDeleted)
            .Error.ShouldBe(AdministrationError.RouteInvalidTransition);
    }

    private static Route ActiveRoute()
    {
        var driver = NewDriver();
        var vehicle = NewVehicle();
        var route = ReadyRoute(driver, vehicle);
        route.Activate(driver, vehicle, 0, Plan(), Now);
        route.ClearDomainEvents();
        return route;
    }
}
