using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Events;
using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Model.ValueObjects;
using RutaSegura.Monolith.Administration.Tests.Support;
using static RutaSegura.Monolith.Administration.Tests.Support.AdministrationMother;

namespace RutaSegura.Monolith.Administration.Tests.Domain;

/// <summary>Where each child is picked up / dropped off: stops and students per stop (US-04).</summary>
public sealed class RouteStopsTests
{
    [Fact]
    public void Create_NewRoute_IsDraftWithoutStopsNorAssignment()
    {
        var route = NewRoute();

        route.Status.IsDraft().ShouldBeTrue();
        route.Stops.ShouldBeEmpty();
        route.Assignment.ShouldBeNull();
        route.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<RouteCreatedEvent>();
    }

    [Fact]
    public void AddStop_WithoutPosition_AppendsWithNextSequence()
    {
        var route = NewRoute();

        var first = route.AddStop("Parada 1", null, StopLocation(0));
        var second = route.AddStop("Parada 2", "Calle Los Olivos 123", StopLocation(1));

        route.Stops.Select(s => s.Id).ShouldBe([first.Id, second.Id], ignoreOrder: false);
        second.Sequence.ShouldBe(2);
        second.Address.ShouldBe("Calle Los Olivos 123");
    }

    [Fact]
    public void AddStop_AtPosition_ShiftsTheFollowingStops()
    {
        var route = NewRoute();
        var a = route.AddStop("A", null, StopLocation(0));
        var c = route.AddStop("C", null, StopLocation(2));

        var b = route.AddStop("B", null, StopLocation(1), position: 2);

        route.Stops.Select(s => (s.Name, s.Sequence)).ShouldBe([("A", 1), ("B", 2), ("C", 3)], ignoreOrder: false);
        a.Sequence.ShouldBe(1);
        b.Sequence.ShouldBe(2);
        c.Sequence.ShouldBe(3);
    }

    [Fact]
    public void AddStop_CloserThanArrivalRadiusToAnotherStop_ThrowsRouteStopTooClose()
    {
        var route = NewRoute();
        route.AddStop("Parada 1", null, new GeoLocation(-12.11000, -76.98500));

        // ~33 m away: Trip & Tracking would consider both stops reached at the same time.
        Should.Throw<AdministrationDomainException>(() =>
                route.AddStop("Parada 2", null, new GeoLocation(-12.11030, -76.98500)))
            .Error.ShouldBe(AdministrationError.RouteStopTooClose);
    }

    [Fact]
    public void AddStop_BeyondTheMaximum_ThrowsRouteStopLimitReached()
    {
        var route = NewRoute();
        for (var i = 0; i < Route.MaxStops; i++)
            route.AddStop($"Parada {i + 1}", null, StopLocation(i));

        Should.Throw<AdministrationDomainException>(() => route.AddStop("Extra", null, StopLocation(Route.MaxStops)))
            .Error.ShouldBe(AdministrationError.RouteStopLimitReached);
    }

    [Fact]
    public void RemoveStop_WithStudents_ThrowsRouteStopHasStudents()
    {
        var route = NewRoute();
        var stop = route.AddStop("Parada 1", null, StopLocation(0));
        route.AssignStudentToStop(stop.Id, new StudentId(Guid.NewGuid()), null);

        Should.Throw<AdministrationDomainException>(() => route.RemoveStop(stop.Id))
            .Error.ShouldBe(AdministrationError.RouteStopHasStudents);
    }

    [Fact]
    public void RemoveStop_Empty_ResequencesTheRemainingStops()
    {
        var route = NewRoute();
        var first = route.AddStop("A", null, StopLocation(0));
        route.AddStop("B", null, StopLocation(1));
        route.AddStop("C", null, StopLocation(2));

        route.RemoveStop(first.Id);

        route.Stops.Select(s => (s.Name, s.Sequence)).ShouldBe([("B", 1), ("C", 2)], ignoreOrder: false);
    }

    [Fact]
    public void ReorderStops_NotEveryStopExactlyOnce_Throws()
    {
        var route = NewRoute();
        var a = route.AddStop("A", null, StopLocation(0));
        route.AddStop("B", null, StopLocation(1));

        Should.Throw<ArgumentException>(() => route.ReorderStops([a.Id, a.Id]));
    }

    [Fact]
    public void ReorderStops_Permutation_AppliesTheNewOrder()
    {
        var route = NewRoute();
        var a = route.AddStop("A", null, StopLocation(0));
        var b = route.AddStop("B", null, StopLocation(1));

        route.ReorderStops([b.Id, a.Id]);

        route.Stops.Select(s => s.Name).ShouldBe(["B", "A"], ignoreOrder: false);
    }

    [Fact]
    public void AssignStudentToStop_StudentAlreadyOnAnotherStop_IsMoved()
    {
        var route = NewRoute();
        var home = route.AddStop("Casa", null, StopLocation(0));
        var grandma = route.AddStop("Casa de la abuela", null, StopLocation(1));
        var mateo = new StudentId(Guid.NewGuid());
        route.AssignStudentToStop(home.Id, mateo, null);

        route.AssignStudentToStop(grandma.Id, mateo, null);

        home.HasStudent(mateo).ShouldBeFalse();
        grandma.HasStudent(mateo).ShouldBeTrue();
        route.StudentCount.ShouldBe(1);
    }

    [Fact]
    public void AssignStudentToStop_BeyondVehicleCapacity_ThrowsCapacityExceeded()
    {
        var route = NewRoute();
        var stop = route.AddStop("Parada 1", null, StopLocation(0));
        var capacity = new SeatingCapacity(2);
        route.AssignStudentToStop(stop.Id, new StudentId(Guid.NewGuid()), capacity);
        route.AssignStudentToStop(stop.Id, new StudentId(Guid.NewGuid()), capacity);

        Should.Throw<AdministrationDomainException>(() =>
                route.AssignStudentToStop(stop.Id, new StudentId(Guid.NewGuid()), capacity))
            .Error.ShouldBe(AdministrationError.VehicleCapacityExceeded);
        route.StudentCount.ShouldBe(2);
    }

    [Fact]
    public void AssignStudentToStop_UnknownStop_ThrowsRouteStopNotFound()
    {
        var route = NewRoute();

        Should.Throw<AdministrationDomainException>(() =>
                route.AssignStudentToStop(RouteStopId.New(), new StudentId(Guid.NewGuid()), null))
            .Error.ShouldBe(AdministrationError.RouteStopNotFound);
    }

    [Fact]
    public void UnassignStudent_NotOnRoute_ThrowsStudentNotOnRoute()
    {
        var route = NewRoute();

        Should.Throw<AdministrationDomainException>(() => route.UnassignStudent(new StudentId(Guid.NewGuid())))
            .Error.ShouldBe(AdministrationError.StudentNotOnRoute);
    }
}
