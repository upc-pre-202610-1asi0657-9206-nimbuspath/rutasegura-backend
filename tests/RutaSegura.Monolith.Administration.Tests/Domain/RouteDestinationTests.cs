using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using static RutaSegura.Monolith.Administration.Tests.Support.AdministrationMother;
// Microsoft.AspNetCore.Routing (implicit global using of the Web SDK) also defines RouteDirection.
using RouteDirection = Monolith.Administration.Domain.Model.ValueObjects.RouteDirection;

namespace RutaSegura.Monolith.Administration.Tests.Domain;

/// <summary>The destination (school) stays at its end of the route while families add their pickup stops.</summary>
public sealed class RouteDestinationTests
{
    private static readonly GeoLocation School = new(-12.1300, -76.9900);
    private static readonly GeoLocation North = new(-12.1000, -76.9850);
    private static readonly GeoLocation BeyondSchool = new(-12.1400, -76.9910);

    private static StudentId Student() => new(Guid.NewGuid());

    private static Route RouteTo(string direction)
    {
        var route = NewRoute();
        route.ChangeDirection(new RouteDirection(direction));
        route.SetDestination("Colegio", "Av. Escolar 100", School);
        return route;
    }

    [Fact]
    public void PickupStops_AreInsertedBeforeTheDestination()
    {
        var route = RouteTo("PICKUP");

        route.PlaceStudent(Student(), North, "Norte", null);
        route.PlaceStudent(Student(), BeyondSchool, "Mas alla del colegio", null);

        route.Stops.Count.ShouldBe(3);
        route.Stops[^1].Origin.IsDestination().ShouldBeTrue();
        route.Stops.Select(stop => stop.Sequence).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void ReturnStops_AreInsertedAfterTheDestination()
    {
        var route = RouteTo("RETURN");

        route.PlaceStudent(Student(), North, "Norte", null);
        route.PlaceStudent(Student(), BeyondSchool, "Mas alla del colegio", null);

        route.Stops.Count.ShouldBe(3);
        route.Stops[0].Origin.IsDestination().ShouldBeTrue();
    }

    [Fact]
    public void AChildLivingNearTheSchool_GetsItsOwnPickupStop()
    {
        var route = RouteTo("PICKUP");

        // About 110 m from the school: within the joining radius of ordinary stops.
        var stop = route.PlaceStudent(Student(), new GeoLocation(-12.1290, -76.9900), "Cerca", null);

        stop.Origin.IsEnrollment().ShouldBeTrue();
        route.Stops.Count.ShouldBe(2);
    }

    [Fact]
    public void AChildNextDoorToTheSchool_UsesTheDestinationStop()
    {
        var route = RouteTo("PICKUP");

        var stop = route.PlaceStudent(Student(), new GeoLocation(-12.1302, -76.9900), "Al lado", null);

        stop.ShouldBe(route.DestinationStop());
        route.Stops.Count.ShouldBe(1);
    }

    [Fact]
    public void ChangingTheDestination_KeepsASingleOne_AndReorderingCannotMoveIt()
    {
        var route = RouteTo("PICKUP");
        var home = route.PlaceStudent(Student(), North, "Norte", null);

        route.SetDestination("Colegio nuevo", null, BeyondSchool);
        var destination = route.DestinationStop()!;
        route.ReorderStops([destination.Id, home.Id]);

        route.Stops.Count(stop => stop.Origin.IsDestination()).ShouldBe(1);
        destination.Name.ShouldBe("Colegio nuevo");
        route.Stops[^1].ShouldBe(destination);
    }
}
