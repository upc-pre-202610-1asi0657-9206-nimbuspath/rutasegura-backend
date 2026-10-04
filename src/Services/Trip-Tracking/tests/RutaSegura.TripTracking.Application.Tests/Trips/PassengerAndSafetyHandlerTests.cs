using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Application.Tests.Fakes;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Domain.Trips.Events;

namespace RutaSegura.TripTracking.Application.Tests.Trips;

[Trait("Category", "Application")]
public class PassengerCommandHandlerTests : TripTrackingTestContext
{
    [Fact]
    public async Task RecordBoarding_ValidStudent_AppliesOnceAndIsIdempotentOnRetry()
    {
        var trip = SeedStartedTrip();
        Clock.SetUtcNow(At(6, 58));
        var handler = new RecordBoardingCommandHandler(Trips, Clock);
        var command = new RecordBoardingCommand(trip.Id.Value, DriverId, Valeria, Guid.NewGuid(),
            Position: new PositionInput(-12.1001, -77.0));

        var first = await handler.HandleAsync(command, Ct);
        var retry = await handler.HandleAsync(command, Ct);

        first.Value.Applied.ShouldBeTrue();
        retry.Value.Applied.ShouldBeFalse();
        trip.DomainEvents.OfType<StudentBoarded>().ShouldHaveSingleItem().Position!.Latitude.ShouldBe(-12.1001);
    }

    [Fact]
    public async Task MarkAbsent_ThenUndo_ReturnsStudentToPending()
    {
        var trip = SeedStartedTrip();

        await new MarkStudentAbsentCommandHandler(Trips, Clock)
            .HandleAsync(new MarkStudentAbsentCommand(trip.Id.Value, DriverId, Mateo, Guid.NewGuid()), Ct);
        await new UndoPassengerRecordCommandHandler(Trips, Clock)
            .HandleAsync(new UndoPassengerRecordCommand(trip.Id.Value, DriverId, Mateo, Guid.NewGuid()), Ct);

        trip.Passengers.Single(p => p.StudentId.Value == Mateo).Status.ShouldBe(PassengerStatus.Pending);
    }

    [Fact]
    public async Task NotifyAbsence_LinkedGuardian_MarksChildAbsent()
    {
        var trip = SeedScheduledTrip();

        var result = await new NotifyAbsenceCommandHandler(Trips, Guardians, Clock)
            .HandleAsync(new NotifyAbsenceCommand(trip.Id.Value, GuardianOfValeria, Valeria, "Cita médica"), Ct);

        result.IsSuccess.ShouldBeTrue();
        var passenger = trip.Passengers.Single(p => p.StudentId.Value == Valeria);
        passenger.Status.ShouldBe(PassengerStatus.Absent);
        passenger.AbsenceSource.ShouldBe(AbsenceSource.Guardian);
    }

    [Fact]
    public async Task NotifyAbsence_GuardianOfAnotherChild_ReturnsForbidden()
    {
        var trip = SeedScheduledTrip();

        var result = await new NotifyAbsenceCommandHandler(Trips, Guardians, Clock)
            .HandleAsync(new NotifyAbsenceCommand(trip.Id.Value, GuardianOfValeria, Mateo, null), Ct);

        result.FirstError.Type.ShouldBe(ErrorType.Forbidden);
        result.FirstError.Code.ShouldBe("TRIP_ACCESS_DENIED");
        trip.Passengers.Single(p => p.StudentId.Value == Mateo).Status.ShouldBe(PassengerStatus.Pending);
    }
}

[Trait("Category", "Application")]
public class SafetyCommandHandlerTests : TripTrackingTestContext
{
    [Fact]
    public async Task ReportIncident_HeavyTraffic_ReturnsDelayedStatus()
    {
        var trip = SeedStartedTrip();
        Clock.SetUtcNow(At(7, 12));

        var result = await new ReportIncidentCommandHandler(Trips, Clock).HandleAsync(
            new ReportIncidentCommand(trip.Id.Value, DriverId, Guid.NewGuid(), IncidentType.HeavyTraffic, null, NotifyGuardians: true), Ct);

        result.Value.TripStatus.ShouldBe(nameof(TripStatus.Delayed));
        trip.DomainEvents.OfType<IncidentReported>().ShouldHaveSingleItem().NotifyGuardians.ShouldBeTrue();
    }

    [Fact]
    public void ReportIncidentValidator_UnknownType_ReturnsError()
    {
        var errors = new ReportIncidentCommandValidator().Validate(
            new ReportIncidentCommand(Guid.NewGuid(), DriverId, Guid.NewGuid(), (IncidentType)99, null, false));

        errors.ShouldHaveSingleItem().Code.ShouldBe("VALIDATION_INCIDENT_TYPE");
    }

    [Fact]
    public async Task RaiseSos_WithCorruptCoordinates_StillRaisesUsingLastKnownPosition()
    {
        var trip = SeedStartedTrip();
        trip.RecordPosition(new DriverId(DriverId),
            Domain.Tracking.GpsReading.Create(Domain.Tracking.GeoPosition.Create(-12.095, -77.0), At(7, 4), 8), At(7, 4));
        Clock.SetUtcNow(At(7, 5));

        var result = await new RaiseSosCommandHandler(Trips, Clock).HandleAsync(
            new RaiseSosCommand(trip.Id.Value, DriverId, Guid.NewGuid(), Position: new PositionInput(0, 0)), Ct);

        result.Value.Applied.ShouldBeTrue();
        trip.DomainEvents.OfType<SosRaised>().ShouldHaveSingleItem().Position!.Latitude.ShouldBe(-12.095);
    }

    [Fact]
    public async Task ResolveSos_ActiveSos_Resolves()
    {
        var trip = SeedStartedTrip();
        trip.RaiseSos(new DriverId(DriverId), ClientEventId.New(), null, null, At(7, 5));

        var result = await new ResolveSosCommandHandler(Trips, Clock)
            .HandleAsync(new ResolveSosCommand(trip.Id.Value, Guid.NewGuid()), Ct);

        result.IsSuccess.ShouldBeTrue();
        trip.ActiveSos.ShouldBeNull();
    }
}
