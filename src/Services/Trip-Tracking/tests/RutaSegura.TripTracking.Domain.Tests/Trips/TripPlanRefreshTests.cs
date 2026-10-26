using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Tests.Builders;
using RutaSegura.TripTracking.Domain.Trips;
using static RutaSegura.TripTracking.Domain.Tests.Builders.TripBuilder;

namespace RutaSegura.TripTracking.Domain.Tests.Trips;

/// <summary>
/// Un viaje programado (hoy o mañana) recibe la planificación nueva de la ruta: alumnos inscritos por sus padres,
/// paradas nuevas o un cambio de conductor aparecen en el siguiente viaje.
/// </summary>
[Trait("Category", "Domain")]
public class TripPlanRefreshTests
{
    private static readonly StopId NewStop = new(Guid.Parse("a0000000-0000-0000-0000-000000000009"));
    private static readonly StudentId Lucia = new(Guid.Parse("b0000000-0000-0000-0000-000000000004"));

    private static TripStopDefinition[] PlanWithNewStop() =>
    [
        new(Stop1, 1, "Av. Primavera 1240", Stop1Position),
        new(NewStop, 2, "Av. Benavides 4500", GeoPosition.Create(-12.0950, -77.0000)),
        new(Stop2, 3, "Jr. Las Begonias 310 (renombrada)", Stop2Position),
        new(School, 4, "Colegio San Agustín", SchoolPosition)
    ];

    [Fact]
    public void RefreshPlan_ScheduledTrip_AddsTheNewStudentAndStop_AndKeepsTheOthers()
    {
        var trip = new TripBuilder().Build();
        trip.NotifyGuardianAbsence(Camila, "Tiene fiebre", ScheduledStart.AddHours(-1));

        var refreshed = trip.RefreshPlan("Ruta 3", OtherDriver, Vehicle, PlanWithNewStop(),
        [
            new TripPassengerDefinition(Valeria, Stop1),
            new TripPassengerDefinition(Camila, Stop2),
            new TripPassengerDefinition(Lucia, NewStop)
        ]);

        refreshed.ShouldBeTrue();
        trip.DriverId.ShouldBe(OtherDriver);
        trip.Stops.Select(s => s.StopId).ShouldBe([Stop1, NewStop, Stop2, School], ignoreOrder: false);
        trip.Stops.Single(s => s.StopId == Stop2).Name.ShouldBe("Jr. Las Begonias 310 (renombrada)");
        trip.Passengers.Select(p => p.StudentId).ShouldBe([Valeria, Camila, Lucia], ignoreOrder: true);
        trip.Passengers.Single(p => p.StudentId == Lucia).Status.ShouldBe(PassengerStatus.Pending);
        trip.Passengers.Single(p => p.StudentId == Camila).Status.ShouldBe(PassengerStatus.Absent,
            "the absence the guardian already reported is kept");
        trip.HasPassenger(Mateo).ShouldBeFalse("Mateo left the route");
    }

    [Fact]
    public void RefreshPlan_MovesAStudentToItsNewStop()
    {
        var trip = new TripBuilder().Build();

        trip.RefreshPlan("Ruta 3", Driver, Vehicle, PlanWithNewStop(),
        [
            new TripPassengerDefinition(Valeria, NewStop),
            new TripPassengerDefinition(Mateo, Stop1),
            new TripPassengerDefinition(Camila, Stop2)
        ]);

        trip.Passengers.Single(p => p.StudentId == Valeria).StopId.ShouldBe(NewStop);
    }

    [Fact]
    public void RefreshPlan_StartedTrip_KeepsItsCopy()
    {
        var trip = new TripBuilder().BuildStarted();

        trip.RefreshPlan("Ruta 3", Driver, Vehicle, PlanWithNewStop(), [new TripPassengerDefinition(Lucia, NewStop)])
            .ShouldBeFalse();

        trip.Passengers.Count.ShouldBe(3);
        trip.Stops.Count.ShouldBe(3);
    }

    [Fact]
    public void RefreshPlan_WithAStudentOnAnUnknownStop_IsRejected()
    {
        var trip = new TripBuilder().Build();

        Should.Throw<RutaSegura.BuildingBlocks.Domain.DomainException>(() =>
            trip.RefreshPlan("Ruta 3", Driver, Vehicle, PlanWithNewStop(),
                [new TripPassengerDefinition(Lucia, new StopId(Guid.NewGuid()))]));
    }
}
