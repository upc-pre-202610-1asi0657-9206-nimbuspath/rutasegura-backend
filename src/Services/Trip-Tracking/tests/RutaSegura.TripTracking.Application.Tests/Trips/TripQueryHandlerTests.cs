using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Application.Tests.Fakes;
using RutaSegura.TripTracking.Application.Trips.Queries;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Tests.Trips;

[Trait("Category", "Application")]
public class GetTripLiveStatusQueryHandlerTests : TripTrackingTestContext
{
    private GetTripLiveStatusQueryHandler Handler => new(Trips, Guardians, Clock);

    private Trip SeedTripWithBusNearStop1()
    {
        var trip = SeedStartedTrip();
        trip.RecordPosition(new DriverId(DriverId),
            GpsReading.Create(GeoPosition.Create(-12.1050, -77.0), At(6, 50), 8, 30), At(6, 50));
        trip.RecordBoarding(new DriverId(DriverId), new StudentId(Mateo), ClientEventId.New(), null, null, At(6, 51));
        return trip;
    }

    [Fact]
    public async Task Handle_LinkedGuardian_SeesBusPositionAndOnlyOwnChild()
    {
        var trip = SeedTripWithBusNearStop1();
        Clock.SetUtcNow(At(6, 50, 5));

        var result = await Handler.HandleAsync(
            new GetTripLiveStatusQuery(trip.Id.Value, new Requester(GuardianOfValeria, RequesterRole.Guardian)), Ct);

        var dto = result.Value;
        dto.Position.ShouldNotBeNull();
        dto.Position.AgeSeconds.ShouldBe(5);
        dto.NextStop.ShouldNotBeNull();
        dto.NextStop.StopId.ShouldBe(Stop1);
        dto.NextStop.EtaMinutes.ShouldBe(2);
        dto.StudentsOnBoard.ShouldBe(1);
        dto.TotalStudents.ShouldBe(3);
        dto.Passengers.ShouldHaveSingleItem().StudentId.ShouldBe(Valeria); // nunca ve a Mateo (AC-01)
    }

    [Fact]
    public async Task Handle_GuardianWithoutChildInTrip_ReturnsAccessDenied()
    {
        var trip = SeedTripWithBusNearStop1();

        var result = await Handler.HandleAsync(
            new GetTripLiveStatusQuery(trip.Id.Value, new Requester(UnrelatedGuardian, RequesterRole.Guardian)), Ct);

        result.FirstError.Code.ShouldBe("TRIP_ACCESS_DENIED");
    }

    [Fact]
    public async Task Handle_CompletedTrip_HidesPosition()
    {
        var trip = SeedTripWithBusNearStop1();
        trip.RecordBoarding(new DriverId(DriverId), new StudentId(Valeria), ClientEventId.New(), null, null, At(6, 52));
        trip.MarkStudentAbsent(new DriverId(DriverId), new StudentId(Camila), ClientEventId.New(), null, At(7, 0));
        trip.Complete(new DriverId(DriverId), ClientEventId.New(), At(7, 50));

        var result = await Handler.HandleAsync(
            new GetTripLiveStatusQuery(trip.Id.Value, new Requester(GuardianOfValeria, RequesterRole.Guardian)), Ct);

        result.Value.Status.ShouldBe(nameof(TripStatus.Completed));
        result.Value.Position.ShouldBeNull();
        result.Value.NextStop.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_AssignedDriver_SeesAllPassengers_OtherDriverDenied()
    {
        var trip = SeedTripWithBusNearStop1();

        var own = await Handler.HandleAsync(new GetTripLiveStatusQuery(trip.Id.Value, new Requester(DriverId, RequesterRole.Driver)), Ct);
        var other = await Handler.HandleAsync(new GetTripLiveStatusQuery(trip.Id.Value, new Requester(OtherDriverId, RequesterRole.Driver)), Ct);

        own.Value.Passengers.Count.ShouldBe(3);
        other.FirstError.Code.ShouldBe("TRIP_ACCESS_DENIED");
    }
}

[Trait("Category", "Application")]
public class TripSummaryAndAgendaQueryHandlerTests : TripTrackingTestContext
{
    [Fact]
    public async Task Summary_CompletedTrip_MatchesFinishScreen()
    {
        var trip = SeedStartedTrip();
        var driver = new DriverId(DriverId);
        trip.RecordBoarding(driver, new StudentId(Valeria), ClientEventId.New(), null, null, At(6, 50));
        trip.RecordBoarding(driver, new StudentId(Mateo), ClientEventId.New(), null, null, At(6, 50));
        trip.MarkStudentAbsent(driver, new StudentId(Camila), ClientEventId.New(), null, At(7, 0));
        trip.ReportIncident(driver, IncidentType.HeavyTraffic, null, true, ClientEventId.New(), At(7, 12), null, At(7, 12));
        trip.Complete(driver, ClientEventId.New(), At(7, 53));

        var result = await new GetTripSummaryQueryHandler(Trips)
            .HandleAsync(new GetTripSummaryQuery(trip.Id.Value, new Requester(DriverId, RequesterRole.Driver)), Ct);

        result.Value.Delivered.ShouldBe(2);
        result.Value.Absent.ShouldHaveSingleItem().StudentId.ShouldBe(Camila);
        result.Value.Incidents.ShouldHaveSingleItem().Type.ShouldBe(nameof(IncidentType.HeavyTraffic));
        result.Value.CompletedAt.ShouldBe(At(7, 53));
    }

    [Fact]
    public async Task Summary_Guardian_IsDenied()
    {
        var trip = SeedStartedTrip();

        var result = await new GetTripSummaryQueryHandler(Trips)
            .HandleAsync(new GetTripSummaryQuery(trip.Id.Value, new Requester(GuardianOfValeria, RequesterRole.Guardian)), Ct);

        result.FirstError.Code.ShouldBe("TRIP_ACCESS_DENIED");
    }

    [Fact]
    public async Task Agenda_ReturnsDriverTripsOfTheDayInOrder_ExcludingCancelled()
    {
        var pickup = SeedScheduledTrip();
        var cancelled = SeedScheduledTrip();
        cancelled.Cancel("Duplicado", At(5, 0));

        var result = await new GetDriverAgendaQueryHandler(Trips)
            .HandleAsync(new GetDriverAgendaQuery(DriverId, At(0, 0), At(0, 0).AddDays(1)), Ct);

        var item = result.Value.ShouldHaveSingleItem();
        item.TripId.ShouldBe(pickup.Id.Value);
        item.StudentCount.ShouldBe(3);
        item.StopCount.ShouldBe(3);
    }
}
