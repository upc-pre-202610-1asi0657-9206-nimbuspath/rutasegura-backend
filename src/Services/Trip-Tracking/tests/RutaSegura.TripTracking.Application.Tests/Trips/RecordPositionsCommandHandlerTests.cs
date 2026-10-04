using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Application.Tests.Fakes;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Domain.Common;

namespace RutaSegura.TripTracking.Application.Tests.Trips;

/// <summary>Flujo GPS → validación de dominio → telemetría persistida → publicación en tiempo real.</summary>
[Trait("Category", "Application")]
public class RecordPositionsCommandHandlerTests : TripTrackingTestContext
{
    private RecordPositionsCommandHandler Handler => new(Trips, TrackPoints, LivePublisher, Clock);

    private static GpsReadingInput Reading(double latitude, DateTimeOffset at, double accuracy = 8, double? speed = 30) =>
        new(latitude, -77.0, at, accuracy, speed, 0);

    [Fact]
    public async Task Handle_ValidReadings_PersistsTrackPointsAndPublishesLatestPositionOnce()
    {
        var trip = SeedStartedTrip();
        Clock.SetUtcNow(At(6, 46));

        var result = await Handler.HandleAsync(new RecordPositionsCommand(trip.Id.Value, DriverId,
        [
            Reading(-12.1200, At(6, 44)),
            Reading(-12.1100, At(6, 45), speed: 30)
        ]), Ct);

        result.Value.Accepted.ShouldBe(2);
        result.Value.LastAcceptedAt.ShouldBe(At(6, 45));
        TrackPoints.Points.Count.ShouldBe(2);

        var update = LivePublisher.Published.ShouldHaveSingleItem();
        update.TripId.ShouldBe(trip.Id.Value);
        update.VehicleId.ShouldBe(VehicleId);
        update.Latitude.ShouldBe(-12.1100);
        update.RecordedAt.ShouldBe(At(6, 45));
        update.NextStopId.ShouldBe(Stop1);
        update.NextStopDistanceMeters!.Value.ShouldBe(1112, tolerance: 1);
        update.NextStopEtaMinutes.ShouldBe(3);
        update.StudentsOnBoard.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_MixedBatch_CountsAcceptedIgnoredAndRejected()
    {
        var trip = SeedStartedTrip();
        Clock.SetUtcNow(At(6, 50));

        var result = await Handler.HandleAsync(new RecordPositionsCommand(trip.Id.Value, DriverId,
        [
            Reading(-12.1200, At(6, 44)),
            Reading(-12.1190, At(6, 45), accuracy: 250), // precisión mala → ignorada
            new GpsReadingInput(0, 0, At(6, 46), 5),     // GPS sin fix → rechazada
            Reading(-12.1150, At(6, 47))
        ]), Ct);

        result.Value.Accepted.ShouldBe(2);
        result.Value.Ignored.ShouldBe(1);
        result.Value.Rejected.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_OfflineBatchOutOfOrder_IsSortedByDeviceTime()
    {
        var trip = SeedStartedTrip();
        Clock.SetUtcNow(At(7, 0));

        var result = await Handler.HandleAsync(new RecordPositionsCommand(trip.Id.Value, DriverId,
        [
            Reading(-12.1150, At(6, 47)),
            Reading(-12.1200, At(6, 44)),
            Reading(-12.1175, At(6, 45, 30))
        ]), Ct);

        result.Value.Accepted.ShouldBe(3);
        TrackPoints.Points.Select(p => p.RecordedAt).ShouldBe(new[] { At(6, 44), At(6, 45, 30), At(6, 47) }, ignoreOrder: false);
        trip.LastPositionAt.ShouldBe(At(6, 47));
    }

    [Fact]
    public async Task Handle_NothingAccepted_DoesNotPublishNorPersist()
    {
        var trip = SeedStartedTrip();
        Clock.SetUtcNow(At(6, 50));

        var result = await Handler.HandleAsync(new RecordPositionsCommand(trip.Id.Value, DriverId,
            [Reading(-12.1200, At(6, 44), accuracy: 300)]), Ct);

        result.Value.Accepted.ShouldBe(0);
        TrackPoints.Points.ShouldBeEmpty();
        LivePublisher.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_TripNotStarted_ThrowsTrackingRequiresActiveTrip()
    {
        var trip = SeedScheduledTrip();

        var ex = await Should.ThrowAsync<DomainException>(() => Handler.HandleAsync(
            new RecordPositionsCommand(trip.Id.Value, DriverId, [Reading(-12.12, At(6, 30))]), Ct));

        ex.Code.ShouldBe(TrackingErrors.RequiresActiveTrip);
        LivePublisher.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_OtherDriver_ReturnsForbidden()
    {
        var trip = SeedStartedTrip();

        var result = await Handler.HandleAsync(new RecordPositionsCommand(trip.Id.Value, OtherDriverId,
            [Reading(-12.12, At(6, 44))]), Ct);

        result.FirstError.Code.ShouldBe(TripErrors.DriverMismatch);
    }

    [Fact]
    public void Validator_EmptyOrOversizedBatch_ReturnsErrors()
    {
        var validator = new RecordPositionsCommandValidator();
        var tooMany = Enumerable.Range(0, 501).Select(i => Reading(-12.12, At(6, 41).AddSeconds(i))).ToList();

        validator.Validate(new RecordPositionsCommand(Guid.NewGuid(), DriverId, [])).ShouldHaveSingleItem()
            .Code.ShouldBe("VALIDATION_READINGS_REQUIRED");
        validator.Validate(new RecordPositionsCommand(Guid.NewGuid(), DriverId, tooMany)).ShouldHaveSingleItem()
            .Code.ShouldBe("VALIDATION_BATCH_TOO_LARGE");
    }
}
