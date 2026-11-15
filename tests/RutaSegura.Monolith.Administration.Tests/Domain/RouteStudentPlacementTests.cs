using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Events;
using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Model.ValueObjects;
using static RutaSegura.Monolith.Administration.Tests.Support.AdministrationMother;

namespace RutaSegura.Monolith.Administration.Tests.Domain;

/// <summary>
///     Route planning driven by the families: a child enrolled by its parent is placed at the stop of its pickup
///     address (Stakeholder → Route Planning facade).
/// </summary>
public sealed class RouteStudentPlacementTests
{
    // ~90 m apart / ~700 m away / ~1.4 km away (Surco).
    private static readonly GeoLocation Primavera = new(-12.11234, -76.98567);
    private static readonly GeoLocation PrimaveraNeighbor = new(-12.11300, -76.98510);
    private static readonly GeoLocation Olivos = new(-12.10890, -76.99120);
    private static readonly GeoLocation Benavides = new(-12.12500, -76.99800);

    private static StudentId Student() => new(Guid.NewGuid());

    [Fact]
    public void PlaceStudent_OnARouteWithoutStops_CreatesAnEnrollmentStopAtThePickupAddress()
    {
        var route = NewRoute();
        var valeria = Student();

        var stop = route.PlaceStudent(valeria, Primavera, "Av. Primavera 1240, Surco", null);

        stop.Origin.IsEnrollment().ShouldBeTrue();
        stop.Name.ShouldBe("Av. Primavera 1240, Surco");
        stop.Address.ShouldBe("Av. Primavera 1240, Surco");
        stop.Location.ShouldBe(Primavera);
        stop.Sequence.ShouldBe(1);
        route.StopOf(valeria).ShouldBe(stop);
    }

    [Fact]
    public void PlaceStudent_NearAnExistingStop_JoinsIt()
    {
        var route = NewRoute();
        var manual = route.AddStop("Parque Primavera", null, Primavera);

        var stop = route.PlaceStudent(Student(), PrimaveraNeighbor, "Jr. Las Begonias 210", null);

        stop.ShouldBe(manual);
        route.Stops.Count.ShouldBe(1);
        manual.Origin.IsEnrollment().ShouldBeFalse();
    }

    [Fact]
    public void PlaceStudent_FarFromEveryStop_InsertsTheStopWhereItAddsTheLeastDistance()
    {
        var route = NewRoute();
        var north = route.AddStop("Norte", null, new GeoLocation(-12.1000, -76.9850));
        var south = route.AddStop("Sur", null, new GeoLocation(-12.1200, -76.9850));

        var middle = route.PlaceStudent(Student(), new GeoLocation(-12.1100, -76.9855), "Al medio", null);

        route.Stops.Select(s => s.Id).ShouldBe([north.Id, middle.Id, south.Id], ignoreOrder: false);
        route.Stops.Select(s => s.Sequence).ShouldBe([1, 2, 3], ignoreOrder: false);
    }

    [Fact]
    public void PlaceStudent_BeyondTheEnds_GoesFirstOrLast()
    {
        var route = NewRoute();
        route.AddStop("A", null, new GeoLocation(-12.1000, -76.9850));
        route.AddStop("B", null, new GeoLocation(-12.1100, -76.9850));

        var beforeA = route.PlaceStudent(Student(), new GeoLocation(-12.0900, -76.9850), "Antes", null);
        var afterB = route.PlaceStudent(Student(), new GeoLocation(-12.1200, -76.9850), "Después", null);

        beforeA.Sequence.ShouldBe(1);
        afterB.Sequence.ShouldBe(4);
    }

    [Fact]
    public void PlaceStudent_Again_WithANewAddress_MovesTheStudent_AndRemovesTheEmptyEnrollmentStop()
    {
        var route = NewRoute();
        var valeria = Student();
        var old = route.PlaceStudent(valeria, Primavera, "Primavera", null);

        var moved = route.PlaceStudent(valeria, Olivos, "Olivos", null);

        moved.ShouldNotBe(old);
        route.Stops.ShouldHaveSingleItem().ShouldBe(moved);
        route.StudentCount.ShouldBe(1);
    }

    [Fact]
    public void PlaceStudent_Again_AtTheSameAddress_IsANoOp()
    {
        var route = NewRoute();
        var valeria = Student();
        var stop = route.PlaceStudent(valeria, Primavera, "Primavera", null);

        route.PlaceStudent(valeria, PrimaveraNeighbor, "Primavera (corregida)", null).ShouldBe(stop);
        route.Stops.Count.ShouldBe(1);
    }

    [Fact]
    public void PlaceStudent_OnAFullVehicle_ThrowsCapacityExceeded()
    {
        var route = NewRoute();
        var capacity = new SeatingCapacity(1);
        route.PlaceStudent(Student(), Primavera, "Primavera", capacity);

        Should.Throw<AdministrationDomainException>(() =>
                route.PlaceStudent(Student(), Olivos, "Olivos", capacity))
            .Error.ShouldBe(AdministrationError.VehicleCapacityExceeded);
        route.Stops.Count.ShouldBe(1, "nothing changes when the rule fails");
    }

    [Fact]
    public void PlaceStudent_AlreadyPlaced_DoesNotNeedAnotherSeat()
    {
        var route = NewRoute();
        var valeria = Student();
        var capacity = new SeatingCapacity(1);
        route.PlaceStudent(valeria, Primavera, "Primavera", capacity);

        Should.NotThrow(() => route.PlaceStudent(valeria, Olivos, "Olivos", capacity));
    }

    [Fact]
    public void PlaceStudent_OnAnInactiveRoute_IsRejected()
    {
        var route = ActiveRoute();
        route.Deactivate();

        Should.Throw<AdministrationDomainException>(() => route.PlaceStudent(Student(), Olivos, "Olivos", null))
            .Error.ShouldBe(AdministrationError.RouteNotAcceptingEnrollments);
        route.AcceptsEnrollments().ShouldBeFalse();
    }

    [Fact]
    public void PlaceStudent_WhenTheRouteHasTheMaximumStops_IsRejected()
    {
        var route = NewRoute();
        for (var i = 0; i < Route.MaxStops; i++)
            route.AddStop($"Parada {i + 1}", null, new GeoLocation(-12.0 - i * 0.003, -76.98));

        Should.Throw<AdministrationDomainException>(() =>
                route.PlaceStudent(Student(), new GeoLocation(-11.5, -76.5), "Lejos", null))
            .Error.ShouldBe(AdministrationError.RouteStopLimitReached);
    }

    [Fact]
    public void PlaceStudent_OnAnActiveRoute_RecordsOneDetailsChangedEvent()
    {
        var route = ActiveRoute();

        route.PlaceStudent(Student(), Benavides, "Benavides", new SeatingCapacity(15));
        route.PlaceStudent(Student(), Olivos, "Olivos", new SeatingCapacity(15));

        route.DomainEvents.OfType<RouteDetailsChangedEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void RemoveStudent_IsIdempotent_AndCleansTheAutomaticStop()
    {
        var route = NewRoute();
        var valeria = Student();
        route.PlaceStudent(valeria, Primavera, "Primavera", null);

        route.RemoveStudent(valeria).ShouldBeTrue();
        route.RemoveStudent(valeria).ShouldBeFalse();
        route.Stops.ShouldBeEmpty();
    }

    [Fact]
    public void RemoveStudent_KeepsManualStops_AndTheLastStopOfAnActiveRoute()
    {
        var route = ActiveRoute();
        var onlyAutomatic = route.Stops.Single(s => s.Origin.IsEnrollment());
        var valeria = onlyAutomatic.Students.Single();
        route.RemoveStop(route.Stops.Single(s => !s.Origin.IsEnrollment()).Id);

        route.RemoveStudent(valeria);

        route.Stops.ShouldHaveSingleItem("an active route keeps at least one stop");
    }

    [Fact]
    public void UnassignStudent_ByTheAdministrator_AlsoCleansAnAutomaticStop()
    {
        var route = NewRoute();
        var valeria = Student();
        route.PlaceStudent(valeria, Primavera, "Primavera", null);

        route.UnassignStudent(valeria);

        route.Stops.ShouldBeEmpty();
    }

    [Fact]
    public void UpdateStop_ByTheAdministrator_TurnsAnAutomaticStopIntoAManualOne()
    {
        var route = NewRoute();
        var valeria = Student();
        var stop = route.PlaceStudent(valeria, Primavera, "Primavera", null);

        route.UpdateStop(stop.Id, "Esquina del parque", null, Primavera);
        route.RemoveStudent(valeria);

        route.Stops.ShouldHaveSingleItem().Origin.IsEnrollment().ShouldBeFalse();
    }

    [Fact]
    public void EnrollmentCode_IsGeneratedOnCreation_AndCanBeRegenerated()
    {
        var route = NewRoute();
        var first = route.EnrollmentCode;

        RouteEnrollmentCode.IsWellFormed(first.Value).ShouldBeTrue();
        first.Value.Length.ShouldBe(RouteEnrollmentCode.Length);
        route.RegenerateEnrollmentCode();
        route.EnrollmentCode.ShouldNotBe(first);
    }

    [Theory]
    [InlineData("k7qm-4p2x", "K7QM4P2X", true)]
    [InlineData(" K7QM 4P2X ", "K7QM4P2X", true)]
    [InlineData("K0QM4P2X", "K0QM4P2X", false)] // 0 is not in the alphabet (looks like O)
    [InlineData("ABC", "ABC", false)]
    public void EnrollmentCode_IsNormalizedAndValidated(string typed, string normalized, bool wellFormed)
    {
        RouteEnrollmentCode.Normalize(typed).ShouldBe(normalized);
        RouteEnrollmentCode.IsWellFormed(normalized).ShouldBe(wellFormed);
    }

    /// <summary>ACTIVE route with a manual stop and an automatic stop holding one student.</summary>
    private static Route ActiveRoute()
    {
        var driver = NewDriver();
        var vehicle = NewVehicle();
        var route = NewRoute(organization: driver.OrganizationId);
        route.AddStop("Parque Primavera", null, Primavera);
        route.PlaceStudent(Student(), Olivos, "Olivos", vehicle.Capacity);
        route.AssignResources(driver, vehicle, Now);
        route.Activate(driver, vehicle, 0, Plan(), Now);
        route.ClearDomainEvents();
        return route;
    }
}
