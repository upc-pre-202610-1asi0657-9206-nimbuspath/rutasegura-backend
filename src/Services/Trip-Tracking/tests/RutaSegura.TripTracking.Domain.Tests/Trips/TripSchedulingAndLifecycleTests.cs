using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tests.Builders;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Domain.Trips.Events;
using static RutaSegura.TripTracking.Domain.Tests.Builders.TripBuilder;

namespace RutaSegura.TripTracking.Domain.Tests.Trips;

[Trait("Category", "Unit")]
public class TripSchedulingTests
{
    private static readonly TripStopDefinition[] OneStop = [new(Stop1, 1, "Parada 1", Stop1Position)];

    [Fact]
    public void Schedule_ValidDefinition_CreatesScheduledTripAndRaisesTripScheduled()
    {
        var id = TripId.New();

        var trip = Trip.Schedule(id, Route, "Ruta 3", TripDirection.Pickup, Driver, Vehicle, ScheduledStart,
            OneStop, [new TripPassengerDefinition(Valeria, Stop1)], ScheduledStart.AddHours(-12));

        trip.Status.ShouldBe(TripStatus.Scheduled);
        trip.Passengers.ShouldHaveSingleItem().Status.ShouldBe(PassengerStatus.Pending);
        var evt = trip.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TripScheduled>();
        evt.TripId.ShouldBe(id);
        evt.ScheduledStart.ShouldBe(ScheduledStart);
    }

    [Fact]
    public void Schedule_WithoutStops_ThrowsInvalidDefinition()
    {
        var ex = Should.Throw<DomainException>(() => Trip.Schedule(TripId.New(), Route, "Ruta 3", TripDirection.Pickup,
            Driver, Vehicle, ScheduledStart, [], [], ScheduledStart));

        ex.Code.ShouldBe(TripErrors.InvalidDefinition);
    }

    [Fact]
    public void Schedule_PassengerAtUnknownStop_ThrowsInvalidDefinition()
    {
        var ex = Should.Throw<DomainException>(() => Trip.Schedule(TripId.New(), Route, "Ruta 3", TripDirection.Pickup,
            Driver, Vehicle, ScheduledStart, OneStop, [new TripPassengerDefinition(Valeria, Stop2)], ScheduledStart));

        ex.Code.ShouldBe(TripErrors.InvalidDefinition);
    }

    [Fact]
    public void Schedule_DuplicatedStudent_ThrowsInvalidDefinition()
    {
        var ex = Should.Throw<DomainException>(() => Trip.Schedule(TripId.New(), Route, "Ruta 3", TripDirection.Pickup,
            Driver, Vehicle, ScheduledStart, OneStop,
            [new TripPassengerDefinition(Valeria, Stop1), new TripPassengerDefinition(Valeria, Stop1)], ScheduledStart));

        ex.Code.ShouldBe(TripErrors.InvalidDefinition);
    }

    [Fact]
    public void Schedule_WithoutDriver_ThrowsInvalidDefinition()
    {
        var ex = Should.Throw<DomainException>(() => Trip.Schedule(TripId.New(), Route, "Ruta 3", TripDirection.Pickup,
            new DriverId(Guid.Empty), Vehicle, ScheduledStart, OneStop, [], ScheduledStart));

        ex.Code.ShouldBe(TripErrors.InvalidDefinition);
    }
}

[Trait("Category", "Unit")]
public class TripStartTests
{
    [Fact]
    public void Start_AssignedDriverOnTime_MovesToInProgressAndRaisesTripStarted()
    {
        var trip = new TripBuilder().Build();

        var applied = trip.Start(Driver, ClientEventId.New(), At(6, 41));

        applied.ShouldBeTrue();
        trip.Status.ShouldBe(TripStatus.InProgress);
        trip.StartedAt.ShouldBe(At(6, 41));
        var evt = trip.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<TripStarted>();
        evt.ExpectedStudents.Count.ShouldBe(3);
    }

    [Fact]
    public void Start_ExcludesStudentsWhoseGuardianNotifiedAbsence()
    {
        var trip = new TripBuilder().Build();
        trip.NotifyGuardianAbsence(Camila, "Tiene cita médica", At(6, 0));

        trip.Start(Driver, ClientEventId.New(), At(6, 40));

        var evt = trip.DomainEvents.OfType<TripStarted>().ShouldHaveSingleItem();
        evt.ExpectedStudents.ShouldNotContain(Camila);
    }

    [Fact]
    public void Start_OtherDriver_ThrowsDriverMismatch()
    {
        var trip = new TripBuilder().Build();

        var ex = Should.Throw<DomainException>(() => trip.Start(OtherDriver, ClientEventId.New(), At(6, 40)));

        ex.Code.ShouldBe(TripErrors.DriverMismatch);
        trip.Status.ShouldBe(TripStatus.Scheduled);
    }

    [Fact]
    public void Start_MoreThanOneHourBeforeSchedule_ThrowsStartTooEarly()
    {
        var trip = new TripBuilder().Build();

        var ex = Should.Throw<DomainException>(() => trip.Start(Driver, ClientEventId.New(), At(5, 39)));

        ex.Code.ShouldBe(TripErrors.StartTooEarly);
    }

    [Fact]
    public void Start_MoreThanTenMinutesLate_StartsDelayedAndRaisesTripDelayed()
    {
        var trip = new TripBuilder().Build();

        trip.Start(Driver, ClientEventId.New(), At(6, 55));

        trip.Status.ShouldBe(TripStatus.Delayed);
        trip.DomainEvents.OfType<TripDelayed>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Start_SameClientEventTwice_IsIdempotent()
    {
        var trip = new TripBuilder().Build();
        var clientEventId = ClientEventId.New();
        trip.Start(Driver, clientEventId, At(6, 40));

        var appliedAgain = trip.Start(Driver, clientEventId, At(6, 41));

        appliedAgain.ShouldBeFalse();
        trip.DomainEvents.OfType<TripStarted>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Start_AlreadyStartedWithNewClientEvent_ThrowsInvalidTransition()
    {
        var trip = new TripBuilder().BuildStarted();

        var ex = Should.Throw<DomainException>(() => trip.Start(Driver, ClientEventId.New(), At(6, 45)));

        ex.Code.ShouldBe(TripErrors.InvalidTransition);
    }
}

[Trait("Category", "Unit")]
public class TripCompletionTests
{
    [Fact]
    public void Complete_PickupWithEveryoneRegistered_DeliversBoardedStudentsAndRaisesTripCompleted()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.RecordBoarding(Driver, Valeria, ClientEventId.New(), At(6, 50), null, At(6, 50));
        trip.RecordBoarding(Driver, Mateo, ClientEventId.New(), At(6, 50), null, At(6, 50));
        trip.MarkStudentAbsent(Driver, Camila, ClientEventId.New(), At(7, 0), At(7, 0));
        trip.ReportIncident(Driver, IncidentType.HeavyTraffic, null, true, ClientEventId.New(), At(7, 12), null, At(7, 12));

        var applied = trip.Complete(Driver, ClientEventId.New(), At(7, 53));

        applied.ShouldBeTrue();
        trip.Status.ShouldBe(TripStatus.Completed);
        trip.Passengers.Count(p => p.Status == PassengerStatus.Alighted).ShouldBe(2);
        var evt = trip.DomainEvents.OfType<TripCompleted>().ShouldHaveSingleItem();
        evt.DeliveredStudents.ShouldBe(new[] { Valeria, Mateo }, ignoreOrder: true);
        evt.AbsentStudents.ShouldHaveSingleItem().ShouldBe(Camila);
        evt.IncidentCount.ShouldBe(1);
        evt.StartedAt.ShouldBe(ScheduledStart);
    }

    [Fact]
    public void Complete_WithPendingStudents_ThrowsHasPendingStudents()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.RecordBoarding(Driver, Valeria, ClientEventId.New(), null, null, At(6, 50));

        var ex = Should.Throw<DomainException>(() => trip.Complete(Driver, ClientEventId.New(), At(7, 50)));

        ex.Code.ShouldBe(TripErrors.HasPendingStudents);
        trip.Status.ShouldBe(TripStatus.InProgress);
    }

    [Fact]
    public void Complete_ReturnTripWithStudentsStillOnBoard_ThrowsHasStudentsOnBoard()
    {
        var trip = new TripBuilder().AsReturnTrip().BuildStarted();
        trip.RecordBoarding(Driver, Valeria, ClientEventId.New(), null, null, At(6, 41));
        trip.RecordBoarding(Driver, Mateo, ClientEventId.New(), null, null, At(6, 41));
        trip.RecordBoarding(Driver, Camila, ClientEventId.New(), null, null, At(6, 41));
        trip.RecordAlighting(Driver, Camila, ClientEventId.New(), null, null, At(7, 0));

        var ex = Should.Throw<DomainException>(() => trip.Complete(Driver, ClientEventId.New(), At(7, 30)));

        ex.Code.ShouldBe(TripErrors.HasStudentsOnBoard);
    }

    [Fact]
    public void Complete_ReturnTripWithEveryoneDroppedOff_Completes()
    {
        var trip = new TripBuilder().AsReturnTrip().BuildStarted();
        foreach (var student in new[] { Valeria, Mateo, Camila })
        {
            trip.RecordBoarding(Driver, student, ClientEventId.New(), null, null, At(6, 41));
            trip.RecordAlighting(Driver, student, ClientEventId.New(), null, null, At(7, 10));
        }

        trip.Complete(Driver, ClientEventId.New(), At(7, 30));

        trip.Status.ShouldBe(TripStatus.Completed);
    }

    [Fact]
    public void Complete_WithActiveSos_ThrowsHasActiveSos()
    {
        var trip = new TripBuilder().BuildStarted();
        foreach (var student in new[] { Valeria, Mateo, Camila })
            trip.RecordBoarding(Driver, student, ClientEventId.New(), null, null, At(6, 50));
        trip.RaiseSos(Driver, ClientEventId.New(), null, null, At(7, 0));

        var ex = Should.Throw<DomainException>(() => trip.Complete(Driver, ClientEventId.New(), At(7, 50)));

        ex.Code.ShouldBe(TripErrors.HasActiveSos);
    }

    [Fact]
    public void Complete_ScheduledTrip_ThrowsInvalidTransition()
    {
        var trip = new TripBuilder().Build();

        var ex = Should.Throw<DomainException>(() => trip.Complete(Driver, ClientEventId.New(), At(7, 50)));

        ex.Code.ShouldBe(TripErrors.InvalidTransition);
    }

    [Fact]
    public void Cancel_ScheduledTrip_CancelsWithReason()
    {
        var trip = new TripBuilder().Build();

        trip.Cancel("Feriado escolar", At(5, 0));

        trip.Status.ShouldBe(TripStatus.Cancelled);
        trip.DomainEvents.OfType<TripCancelled>().ShouldHaveSingleItem().Reason.ShouldBe("Feriado escolar");
    }

    [Fact]
    public void Cancel_InProgressTrip_ThrowsInvalidTransition()
    {
        var trip = new TripBuilder().BuildStarted();

        Should.Throw<DomainException>(() => trip.Cancel("Error", At(6, 45))).Code.ShouldBe(TripErrors.InvalidTransition);
    }

    [Fact]
    public void Cancel_WithoutReason_ThrowsReasonRequired()
    {
        var trip = new TripBuilder().Build();

        Should.Throw<DomainException>(() => trip.Cancel("  ", At(5, 0))).Code.ShouldBe(TripErrors.CancellationReasonRequired);
    }
}
