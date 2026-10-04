using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.TripTracking.Application.Tests.Fakes;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Domain.Trips.Events;

namespace RutaSegura.TripTracking.Application.Tests.Trips;

[Trait("Category", "Application")]
public class ScheduleTripCommandHandlerTests : TripTrackingTestContext
{
    private ScheduleTripCommandHandler Handler => new(Routes, Trips, Clock);

    [Fact]
    public async Task Handle_RouteOperatingThatDay_SchedulesTripAtLimaDepartureTime()
    {
        Routes.Routes[RouteId] = Route3();

        var result = await Handler.HandleAsync(new ScheduleTripCommand(RouteId, new DateOnly(2026, 10, 5)), Ct);

        result.IsSuccess.ShouldBeTrue();
        var trip = Trips.Trips.ShouldHaveSingleItem();
        trip.Id.Value.ShouldBe(result.Value);
        trip.ScheduledStart.ShouldBe(new DateTimeOffset(2026, 10, 5, 6, 40, 0, Lima));
        trip.ScheduledStart.Offset.ShouldBe(Lima);
        trip.Passengers.Count.ShouldBe(3);
        trip.Stops.Count.ShouldBe(3);
        trip.DomainEvents.OfType<TripScheduled>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Handle_SameRouteAndDateTwice_IsIdempotent()
    {
        Routes.Routes[RouteId] = Route3();
        var command = new ScheduleTripCommand(RouteId, new DateOnly(2026, 10, 5));

        var first = await Handler.HandleAsync(command, Ct);
        var second = await Handler.HandleAsync(command, Ct);

        second.Value.ShouldBe(first.Value);
        Trips.Trips.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_UnknownRoute_ReturnsNotFound()
    {
        var result = await Handler.HandleAsync(new ScheduleTripCommand(RouteId, new DateOnly(2026, 10, 5)), Ct);

        result.FirstError.Type.ShouldBe(ErrorType.NotFound);
        result.FirstError.Code.ShouldBe("ROUTE_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_Saturday_ReturnsRouteNotOperating()
    {
        Routes.Routes[RouteId] = Route3();

        var result = await Handler.HandleAsync(new ScheduleTripCommand(RouteId, new DateOnly(2026, 10, 10)), Ct);

        result.FirstError.Code.ShouldBe("ROUTE_NOT_OPERATING");
        Trips.Trips.ShouldBeEmpty();
    }
}

[Trait("Category", "Application")]
public class StartTripCommandHandlerTests : TripTrackingTestContext
{
    private StartTripCommandHandler Handler => new(Trips, Clock);

    [Fact]
    public async Task Handle_AssignedDriver_StartsTripAndLeavesEventForOutbox()
    {
        var trip = SeedScheduledTrip();
        Clock.SetUtcNow(At(6, 41));

        var result = await Handler.HandleAsync(new StartTripCommand(trip.Id.Value, DriverId, Guid.NewGuid()), Ct);

        result.Value.Applied.ShouldBeTrue();
        result.Value.TripStatus.ShouldBe(nameof(TripStatus.InProgress));
        trip.StartedAt.ShouldBe(At(6, 41));
        trip.DomainEvents.OfType<TripStarted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Handle_RetryWithSameClientEventId_ReturnsNotApplied()
    {
        var trip = SeedScheduledTrip();
        var command = new StartTripCommand(trip.Id.Value, DriverId, Guid.NewGuid());
        await Handler.HandleAsync(command, Ct);

        var retry = await Handler.HandleAsync(command, Ct);

        retry.IsSuccess.ShouldBeTrue();
        retry.Value.Applied.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_UnknownTrip_ReturnsNotFound()
    {
        var result = await Handler.HandleAsync(new StartTripCommand(Guid.NewGuid(), DriverId, Guid.NewGuid()), Ct);

        result.FirstError.Code.ShouldBe("TRIP_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_OtherDriver_ReturnsForbiddenWithoutTouchingTrip()
    {
        var trip = SeedScheduledTrip();

        var result = await Handler.HandleAsync(new StartTripCommand(trip.Id.Value, OtherDriverId, Guid.NewGuid()), Ct);

        result.FirstError.Type.ShouldBe(ErrorType.Forbidden);
        result.FirstError.Code.ShouldBe(TripErrors.DriverMismatch);
        trip.Status.ShouldBe(TripStatus.Scheduled);
    }
}

[Trait("Category", "Application")]
public class CompleteAndCancelTripCommandHandlerTests : TripTrackingTestContext
{
    [Fact]
    public async Task Complete_AllStudentsRegistered_CompletesTrip()
    {
        var trip = SeedStartedTrip();
        foreach (var student in new[] { Valeria, Mateo, Camila })
            trip.RecordBoarding(new DriverId(DriverId), new StudentId(student), ClientEventId.New(), null, null, At(6, 55));
        Clock.SetUtcNow(At(7, 53));

        var result = await new CompleteTripCommandHandler(Trips, Clock)
            .HandleAsync(new CompleteTripCommand(trip.Id.Value, DriverId, Guid.NewGuid()), Ct);

        result.Value.TripStatus.ShouldBe(nameof(TripStatus.Completed));
        trip.DomainEvents.OfType<TripCompleted>().ShouldHaveSingleItem().DeliveredStudents.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Cancel_ScheduledTrip_Cancels()
    {
        var trip = SeedScheduledTrip();

        var result = await new CancelTripCommandHandler(Trips, Clock)
            .HandleAsync(new CancelTripCommand(trip.Id.Value, "Feriado escolar"), Ct);

        result.IsSuccess.ShouldBeTrue();
        trip.Status.ShouldBe(TripStatus.Cancelled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CancelValidator_EmptyReason_ReturnsError(string reason)
    {
        var errors = new CancelTripCommandValidator().Validate(new CancelTripCommand(Guid.NewGuid(), reason)).ToList();

        errors.ShouldHaveSingleItem().Code.ShouldBe(TripErrors.CancellationReasonRequired);
    }
}
