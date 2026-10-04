using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tests.Builders;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Domain.Trips.Events;
using static RutaSegura.TripTracking.Domain.Tests.Builders.TripBuilder;

namespace RutaSegura.TripTracking.Domain.Tests.Trips;

[Trait("Category", "Unit")]
public class TripPassengerTests
{
    [Fact]
    public void RecordBoarding_StudentInRoster_BoardsAndRaisesStudentBoarded()
    {
        var trip = new TripBuilder().BuildStarted();
        var position = GeoPosition.Create(-12.1001, -77.0);

        var applied = trip.RecordBoarding(Driver, Valeria, ClientEventId.New(), At(6, 58), position, At(6, 58, 5));

        applied.ShouldBeTrue();
        var passenger = trip.Passengers.Single(p => p.StudentId == Valeria);
        passenger.Status.ShouldBe(PassengerStatus.Boarded);
        passenger.BoardedAt.ShouldBe(At(6, 58));
        var evt = trip.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<StudentBoarded>();
        evt.StopId.ShouldBe(Stop1);
        evt.Position.ShouldBe(position);
        evt.OccurredOn.ShouldBe(At(6, 58));
    }

    [Fact]
    public void RecordBoarding_WithoutPosition_UsesLastKnownBusPosition()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.RecordPosition(Driver, Reading(-12.1001, At(6, 57)), At(6, 57));

        trip.RecordBoarding(Driver, Valeria, ClientEventId.New(), null, null, At(6, 58));

        trip.Passengers.Single(p => p.StudentId == Valeria).BoardedPosition.ShouldBe(trip.LastPosition);
    }

    [Fact]
    public void RecordBoarding_SameClientEventId_IsIdempotent()
    {
        var trip = new TripBuilder().BuildStarted();
        var clientEventId = ClientEventId.New();
        trip.RecordBoarding(Driver, Valeria, clientEventId, null, null, At(6, 58));

        var again = trip.RecordBoarding(Driver, Valeria, clientEventId, null, null, At(6, 59));

        again.ShouldBeFalse();
        trip.DomainEvents.OfType<StudentBoarded>().ShouldHaveSingleItem();
    }

    [Fact]
    public void RecordBoarding_AlreadyBoardedWithNewClientEvent_IsNoOp()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.RecordBoarding(Driver, Valeria, ClientEventId.New(), null, null, At(6, 58));

        var again = trip.RecordBoarding(Driver, Valeria, ClientEventId.New(), null, null, At(6, 59));

        again.ShouldBeFalse();
        trip.DomainEvents.OfType<StudentBoarded>().ShouldHaveSingleItem();
    }

    [Fact]
    public void RecordBoarding_StudentNotInRoster_ThrowsStudentNotInRoster()
    {
        var trip = new TripBuilder().BuildStarted();

        Should.Throw<DomainException>(() => trip.RecordBoarding(Driver, Stranger, ClientEventId.New(), null, null, At(6, 58)))
            .Code.ShouldBe(TripErrors.StudentNotInRoster);
    }

    [Fact]
    public void RecordBoarding_TripNotStarted_ThrowsNotActive()
    {
        var trip = new TripBuilder().Build();

        Should.Throw<DomainException>(() => trip.RecordBoarding(Driver, Valeria, ClientEventId.New(), null, null, At(6, 30)))
            .Code.ShouldBe(TripErrors.NotActive);
    }

    [Fact]
    public void RecordBoarding_TimestampInTheFuture_ThrowsEventTimestampInvalid()
    {
        var trip = new TripBuilder().BuildStarted();

        Should.Throw<DomainException>(() => trip.RecordBoarding(Driver, Valeria, ClientEventId.New(), At(7, 30), null, At(7, 0)))
            .Code.ShouldBe(TripErrors.EventTimestampInvalid);
    }

    [Fact]
    public void RecordBoarding_RejectedAction_DoesNotConsumeClientEventId()
    {
        var trip = new TripBuilder().BuildStarted();
        var clientEventId = ClientEventId.New();
        Should.Throw<DomainException>(() => trip.RecordBoarding(Driver, Valeria, clientEventId, At(7, 30), null, At(7, 0)));

        // El reintento con la hora corregida debe aplicarse, no tratarse como duplicado.
        trip.RecordBoarding(Driver, Valeria, clientEventId, At(7, 0), null, At(7, 0)).ShouldBeTrue();
    }

    [Fact]
    public void RecordBoarding_StudentWhoseGuardianNotifiedAbsence_CanStillBoard()
    {
        var trip = new TripBuilder().Build();
        trip.NotifyGuardianAbsence(Camila, "Está enferma", At(6, 0));
        trip.Start(Driver, ClientEventId.New(), At(6, 40));

        trip.RecordBoarding(Driver, Camila, ClientEventId.New(), null, null, At(7, 5)).ShouldBeTrue();

        var passenger = trip.Passengers.Single(p => p.StudentId == Camila);
        passenger.Status.ShouldBe(PassengerStatus.Boarded);
        passenger.AbsenceSource.ShouldBeNull();
    }

    [Fact]
    public void RecordAlighting_BoardedStudent_AlightsAndRaisesEvent()
    {
        var trip = new TripBuilder().AsReturnTrip().BuildStarted();
        trip.RecordBoarding(Driver, Valeria, ClientEventId.New(), null, null, At(6, 41));

        trip.RecordAlighting(Driver, Valeria, ClientEventId.New(), At(7, 10), null, At(7, 10)).ShouldBeTrue();

        trip.Passengers.Single(p => p.StudentId == Valeria).Status.ShouldBe(PassengerStatus.Alighted);
        trip.DomainEvents.OfType<StudentAlighted>().ShouldHaveSingleItem().StopId.ShouldBe(Stop1);
    }

    [Fact]
    public void RecordAlighting_PendingStudent_ThrowsPassengerInvalidTransition()
    {
        var trip = new TripBuilder().BuildStarted();

        Should.Throw<DomainException>(() => trip.RecordAlighting(Driver, Valeria, ClientEventId.New(), null, null, At(7, 0)))
            .Code.ShouldBe(TripErrors.PassengerInvalidTransition);
    }

    [Fact]
    public void MarkStudentAbsent_PendingStudent_MarksAbsentByDriver()
    {
        var trip = new TripBuilder().BuildStarted();

        trip.MarkStudentAbsent(Driver, Mateo, ClientEventId.New(), null, At(6, 59)).ShouldBeTrue();

        var passenger = trip.Passengers.Single(p => p.StudentId == Mateo);
        passenger.Status.ShouldBe(PassengerStatus.Absent);
        passenger.AbsenceSource.ShouldBe(AbsenceSource.Driver);
        trip.DomainEvents.OfType<StudentMarkedAbsent>().ShouldHaveSingleItem().Source.ShouldBe(AbsenceSource.Driver);
    }

    [Fact]
    public void MarkStudentAbsent_BoardedStudent_ThrowsPassengerInvalidTransition()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.RecordBoarding(Driver, Mateo, ClientEventId.New(), null, null, At(6, 58));

        Should.Throw<DomainException>(() => trip.MarkStudentAbsent(Driver, Mateo, ClientEventId.New(), null, At(6, 59)))
            .Code.ShouldBe(TripErrors.PassengerInvalidTransition);
    }

    [Fact]
    public void NotifyGuardianAbsence_BeforeStart_MarksAbsentWithNote()
    {
        var trip = new TripBuilder().Build();

        trip.NotifyGuardianAbsence(Camila, "  Tiene fiebre  ", At(6, 0)).ShouldBeTrue();

        var passenger = trip.Passengers.Single(p => p.StudentId == Camila);
        passenger.AbsenceSource.ShouldBe(AbsenceSource.Guardian);
        passenger.AbsenceNote.ShouldBe("Tiene fiebre");
    }

    [Fact]
    public void NotifyGuardianAbsence_CancelledTrip_ThrowsInvalidTransition()
    {
        var trip = new TripBuilder().Build();
        trip.Cancel("Feriado", At(5, 0));

        Should.Throw<DomainException>(() => trip.NotifyGuardianAbsence(Camila, null, At(6, 0)))
            .Code.ShouldBe(TripErrors.InvalidTransition);
    }

    [Fact]
    public void UndoPassengerRecord_Boarded_ReturnsToPending()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.RecordBoarding(Driver, Valeria, ClientEventId.New(), null, null, At(6, 58));

        trip.UndoPassengerRecord(Driver, Valeria, ClientEventId.New(), At(6, 58, 30)).ShouldBeTrue();

        var passenger = trip.Passengers.Single(p => p.StudentId == Valeria);
        passenger.Status.ShouldBe(PassengerStatus.Pending);
        passenger.BoardedAt.ShouldBeNull();
        trip.DomainEvents.OfType<PassengerRecordUndone>().ShouldHaveSingleItem().PreviousStatus.ShouldBe(PassengerStatus.Boarded);
    }

    [Fact]
    public void UndoPassengerRecord_GuardianAbsence_CannotBeUndoneByDriver()
    {
        var trip = new TripBuilder().Build();
        trip.NotifyGuardianAbsence(Camila, null, At(6, 0));
        trip.Start(Driver, ClientEventId.New(), At(6, 40));

        Should.Throw<DomainException>(() => trip.UndoPassengerRecord(Driver, Camila, ClientEventId.New(), At(6, 50)))
            .Code.ShouldBe(TripErrors.PassengerInvalidTransition);
    }
}
