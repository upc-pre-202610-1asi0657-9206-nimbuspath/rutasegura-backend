using RutaSegura.TripTracking.Application.Tests.Fakes;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Tests.Trips;

/// <summary>AC-06 Conectividad intermitente: "Sin señal, el reporte se envía al reconectar".</summary>
[Trait("Category", "Application")]
public class SyncOfflineEventsCommandHandlerTests : TripTrackingTestContext
{
    private SyncOfflineEventsCommandHandler Handler => new(Trips, Clock);

    [Fact]
    public async Task Handle_EventsArriveOutOfOrder_AppliesThemByOccurredAt()
    {
        var trip = SeedStartedTrip(TripDirection.Return);
        Clock.SetUtcNow(At(7, 30));
        var alight = new OfflineEventInput(Guid.NewGuid(), OfflineEventType.Alighting, At(7, 10), Valeria);
        var board = new OfflineEventInput(Guid.NewGuid(), OfflineEventType.Boarding, At(6, 42), Valeria);

        var result = await Handler.HandleAsync(new SyncOfflineEventsCommand(trip.Id.Value, DriverId, [alight, board]), Ct);

        result.Value.Applied.ShouldBe(2);
        result.Value.Outcomes[0].ClientEventId.ShouldBe(alight.ClientEventId); // mismo orden que el envío
        var passenger = trip.Passengers.Single(p => p.StudentId.Value == Valeria);
        passenger.Status.ShouldBe(PassengerStatus.Alighted);
        passenger.BoardedAt.ShouldBe(At(6, 42));
        passenger.AlightedAt.ShouldBe(At(7, 10));
    }

    [Fact]
    public async Task Handle_ResentBatch_ReportsDuplicatesWithoutReapplying()
    {
        var trip = SeedStartedTrip();
        Clock.SetUtcNow(At(7, 0));
        var events = new[]
        {
            new OfflineEventInput(Guid.NewGuid(), OfflineEventType.Boarding, At(6, 50), Valeria),
            new OfflineEventInput(Guid.NewGuid(), OfflineEventType.Incident, At(6, 55), IncidentType: IncidentType.HeavyTraffic)
        };
        await Handler.HandleAsync(new SyncOfflineEventsCommand(trip.Id.Value, DriverId, events), Ct);

        var resend = await Handler.HandleAsync(new SyncOfflineEventsCommand(trip.Id.Value, DriverId, events), Ct);

        resend.Value.Duplicates.ShouldBe(2);
        trip.Incidents.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_InvalidEvent_IsRejectedWithCodeAndDoesNotBlockTheRest()
    {
        var trip = SeedStartedTrip();
        Clock.SetUtcNow(At(7, 0));
        var stranger = new OfflineEventInput(Guid.NewGuid(), OfflineEventType.Boarding, At(6, 50), Guid.NewGuid());
        var valid = new OfflineEventInput(Guid.NewGuid(), OfflineEventType.Absent, At(6, 51), Camila);

        var result = await Handler.HandleAsync(new SyncOfflineEventsCommand(trip.Id.Value, DriverId, [stranger, valid]), Ct);

        result.Value.Outcomes[0].Status.ShouldBe(OfflineEventStatus.Rejected);
        result.Value.Outcomes[0].ErrorCode.ShouldBe(TripErrors.StudentNotInRoster);
        result.Value.Outcomes[1].Status.ShouldBe(OfflineEventStatus.Applied);
        trip.Passengers.Single(p => p.StudentId.Value == Camila).Status.ShouldBe(PassengerStatus.Absent);
    }

    [Fact]
    public async Task Handle_SameEventTwiceInsideTheBatch_SecondIsDuplicate()
    {
        var trip = SeedStartedTrip();
        Clock.SetUtcNow(At(7, 0));
        var boarding = new OfflineEventInput(Guid.NewGuid(), OfflineEventType.Boarding, At(6, 50), Mateo);

        var result = await Handler.HandleAsync(new SyncOfflineEventsCommand(trip.Id.Value, DriverId, [boarding, boarding]), Ct);

        result.Value.Applied.ShouldBe(1);
        result.Value.Duplicates.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_OtherDriver_ReturnsForbidden()
    {
        var trip = SeedStartedTrip();

        var result = await Handler.HandleAsync(new SyncOfflineEventsCommand(trip.Id.Value, OtherDriverId,
            [new OfflineEventInput(Guid.NewGuid(), OfflineEventType.Sos, At(6, 50))]), Ct);

        result.FirstError.Code.ShouldBe(TripErrors.DriverMismatch);
    }

    [Fact]
    public void Validator_PassengerEventWithoutStudent_ReturnsError()
    {
        var errors = new SyncOfflineEventsCommandValidator().Validate(new SyncOfflineEventsCommand(Guid.NewGuid(), DriverId,
            [new OfflineEventInput(Guid.NewGuid(), OfflineEventType.Boarding, At(6, 50))])).ToList();

        errors.ShouldHaveSingleItem().Code.ShouldBe("VALIDATION_STUDENT_REQUIRED");
    }
}
