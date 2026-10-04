using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tests.Builders;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Domain.Trips.Events;
using static RutaSegura.TripTracking.Domain.Tests.Builders.TripBuilder;

namespace RutaSegura.TripTracking.Domain.Tests.Trips;

[Trait("Category", "Unit")]
public class TripIncidentTests
{
    [Fact]
    public void ReportIncident_HeavyTraffic_RecordsIncidentAndDelaysTrip()
    {
        var trip = new TripBuilder().BuildStarted();

        trip.ReportIncident(Driver, IncidentType.HeavyTraffic, null, notifyGuardians: true,
            ClientEventId.New(), At(7, 12), null, At(7, 12)).ShouldBeTrue();

        trip.Incidents.ShouldHaveSingleItem().Type.ShouldBe(IncidentType.HeavyTraffic);
        trip.Status.ShouldBe(TripStatus.Delayed);
        var evt = trip.DomainEvents.OfType<IncidentReported>().ShouldHaveSingleItem();
        evt.NotifyGuardians.ShouldBeTrue();
        evt.AffectedStudents.Count.ShouldBe(3);
        trip.DomainEvents.OfType<TripDelayed>().ShouldHaveSingleItem();
    }

    [Fact]
    public void ReportIncident_StudentUnwell_DoesNotDelayTrip()
    {
        var trip = new TripBuilder().BuildStarted();

        trip.ReportIncident(Driver, IncidentType.StudentUnwell, "Mateo con náuseas", false,
            ClientEventId.New(), null, null, At(7, 0));

        trip.Status.ShouldBe(TripStatus.InProgress);
        trip.DomainEvents.OfType<TripDelayed>().ShouldBeEmpty();
    }

    [Fact]
    public void ReportIncident_OtherWithoutDetail_ThrowsDetailRequired()
    {
        var trip = new TripBuilder().BuildStarted();

        Should.Throw<DomainException>(() => trip.ReportIncident(Driver, IncidentType.Other, "   ", false,
                ClientEventId.New(), null, null, At(7, 0)))
            .Code.ShouldBe(TripErrors.IncidentDetailRequired);
    }

    [Fact]
    public void ReportIncident_DetailTooLong_ThrowsDetailTooLong()
    {
        var trip = new TripBuilder().BuildStarted();

        Should.Throw<DomainException>(() => trip.ReportIncident(Driver, IncidentType.Other, new string('x', 501), false,
                ClientEventId.New(), null, null, At(7, 0)))
            .Code.ShouldBe(TripErrors.IncidentDetailTooLong);
    }

    [Fact]
    public void ReportIncident_SameClientEventId_IsIdempotent()
    {
        var trip = new TripBuilder().BuildStarted();
        var id = ClientEventId.New();

        trip.ReportIncident(Driver, IncidentType.HeavyTraffic, null, true, id, null, null, At(7, 0));
        trip.ReportIncident(Driver, IncidentType.HeavyTraffic, null, true, id, null, null, At(7, 1)).ShouldBeFalse();

        trip.Incidents.Count.ShouldBe(1);
    }
}

[Trait("Category", "Unit")]
public class TripSosTests
{
    [Fact]
    public void RaiseSos_ActiveTrip_RaisesSosWithStudentsOnBoard()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.RecordBoarding(Driver, Valeria, ClientEventId.New(), null, null, At(6, 58));
        trip.RecordBoarding(Driver, Mateo, ClientEventId.New(), null, null, At(6, 58));
        var position = GeoPosition.Create(-12.095, -77.0);

        trip.RaiseSos(Driver, ClientEventId.New(), At(7, 5), position, At(7, 5)).ShouldBeTrue();

        trip.ActiveSos.ShouldNotBeNull();
        var evt = trip.DomainEvents.OfType<SosRaised>().ShouldHaveSingleItem();
        evt.Position.ShouldBe(position);
        evt.StudentsOnBoard.ShouldBe(new[] { Valeria, Mateo }, ignoreOrder: true);
        evt.VehicleId.ShouldBe(Vehicle);
    }

    [Fact]
    public void RaiseSos_WithoutAnyGpsFix_StillRaisesSos()
    {
        var trip = new TripBuilder().BuildStarted();

        trip.RaiseSos(Driver, ClientEventId.New(), null, null, At(7, 5)).ShouldBeTrue();

        trip.DomainEvents.OfType<SosRaised>().ShouldHaveSingleItem().Position.ShouldBeNull();
    }

    [Fact]
    public void RaiseSos_WhileAnotherIsActive_DoesNotDuplicate()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.RaiseSos(Driver, ClientEventId.New(), null, null, At(7, 5));

        trip.RaiseSos(Driver, ClientEventId.New(), null, null, At(7, 6)).ShouldBeFalse();

        trip.SosAlerts.Count.ShouldBe(1);
    }

    [Fact]
    public void RaiseSos_TripNotStarted_ThrowsNotActive()
    {
        var trip = new TripBuilder().Build();

        Should.Throw<DomainException>(() => trip.RaiseSos(Driver, ClientEventId.New(), null, null, At(6, 30)))
            .Code.ShouldBe(TripErrors.NotActive);
    }

    [Fact]
    public void ResolveSos_ActiveSos_ResolvesAndAllowsNewOne()
    {
        var trip = new TripBuilder().BuildStarted();
        var operatorId = Guid.NewGuid();
        trip.RaiseSos(Driver, ClientEventId.New(), null, null, At(7, 5));

        trip.ResolveSos(operatorId, At(7, 20));

        trip.ActiveSos.ShouldBeNull();
        trip.DomainEvents.OfType<SosResolved>().ShouldHaveSingleItem().ResolvedBy.ShouldBe(operatorId);
        trip.RaiseSos(Driver, ClientEventId.New(), null, null, At(7, 25)).ShouldBeTrue();
    }

    [Fact]
    public void ResolveSos_WithoutActiveSos_ThrowsNoActiveSos()
    {
        var trip = new TripBuilder().BuildStarted();

        Should.Throw<DomainException>(() => trip.ResolveSos(Guid.NewGuid(), At(7, 0)))
            .Code.ShouldBe(TripErrors.NoActiveSos);
    }
}
