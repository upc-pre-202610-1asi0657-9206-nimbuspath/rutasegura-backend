using Reqnroll;
using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Bdd.Tests.Support;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Domain.Trips.Events;
using static RutaSegura.TripTracking.Bdd.Tests.Support.TripScenarioState;

namespace RutaSegura.TripTracking.Bdd.Tests.Steps;

[Binding]
public sealed class Us11PassengerSteps
{
    private readonly TripScenarioState _state;

    public Us11PassengerSteps(TripScenarioState state)
    {
        _state = state;
    }

    [Given("un viaje activo con un alumno asignado")]
    public void GivenActiveTripWithAssignedStudent()
    {
        _state.Trip = CreateStartedTrip();
        _state.CurrentStudent = Valeria;
    }

    [Given("el alumno se encuentra pendiente de abordar")]
    public void GivenStudentIsPending()
    {
        _state.Trip.Passengers
            .Single(p => p.StudentId == _state.CurrentStudent)
            .Status.ShouldBe(PassengerStatus.Pending);
    }

    [When("el conductor registra el abordaje del alumno")]
    public void WhenDriverRecordsBoarding()
    {
        _state.ActionTime = At(6, 58);
        _state.EventPosition = GeoPosition.Create(-12.1001, -77.0000);
        _state.ClientEventId = ClientEventId.New();

        _state.OperationApplied = _state.Trip.RecordBoarding(
            Driver,
            _state.CurrentStudent,
            _state.ClientEventId,
            _state.ActionTime,
            _state.EventPosition,
            At(6, 58, 5));
    }

    [Then("el estado del alumno debe cambiar a Boarded")]
    public void ThenStudentStatusIsBoarded()
    {
        _state.Trip.Passengers
            .Single(p => p.StudentId == _state.CurrentStudent)
            .Status.ShouldBe(PassengerStatus.Boarded);
    }

    [Then("debe conservarse la fecha del abordaje")]
    public void ThenBoardingTimeIsPreserved()
    {
        _state.Trip.Passengers
            .Single(p => p.StudentId == _state.CurrentStudent)
            .BoardedAt.ShouldBe(_state.ActionTime);
    }

    [Then("debe conservarse la ubicación cuando esté disponible")]
    public void ThenBoardingPositionIsPreserved()
    {
        _state.Trip.Passengers
            .Single(p => p.StudentId == _state.CurrentStudent)
            .BoardedPosition.ShouldBe(_state.EventPosition);
    }

    [Then("debe generarse un evento de dominio StudentBoarded")]
    public void ThenStudentBoardedEventIsRaised()
    {
        _state.Trip.DomainEvents
            .OfType<StudentBoarded>()
            .ShouldHaveSingleItem();
    }

    [Given("un alumno que no está asignado al viaje")]
    public void GivenStudentIsNotAssignedToTrip()
    {
        _state.CurrentStudent = Stranger;
    }

    [When("el conductor intenta registrar su abordaje")]
    public void WhenDriverAttemptsToBoardUnknownStudent()
    {
        try
        {
            _state.Trip.RecordBoarding(
                Driver,
                _state.CurrentStudent,
                ClientEventId.New(),
                null,
                null,
                At(6, 58));
        }
        catch (DomainException ex)
        {
            _state.ErrorCode = ex.Code;
        }
    }

    [Then("la acción debe rechazarse con StudentNotInRoster")]
    public void ThenActionIsRejectedWithStudentNotInRoster()
    {
        _state.ErrorCode.ShouldBe(TripErrors.StudentNotInRoster);
    }

    [Given("el abordaje ya fue registrado con un clientEventId")]
    public void GivenBoardingWasAlreadyRecordedWithClientEventId()
    {
        _state.CurrentStudent = Valeria;
        _state.ClientEventId = ClientEventId.New();

        _state.Trip.RecordBoarding(
            Driver,
            _state.CurrentStudent,
            _state.ClientEventId,
            null,
            null,
            At(6, 58));

        _state.RepeatOperation = () =>
            _state.Trip.RecordBoarding(
                Driver,
                _state.CurrentStudent,
                _state.ClientEventId,
                null,
                null,
                At(6, 59));
    }

    [Then("no debe generarse otro evento StudentBoarded")]
    public void ThenAnotherStudentBoardedEventIsNotRaised()
    {
        _state.Trip.DomainEvents
            .OfType<StudentBoarded>()
            .Count()
            .ShouldBe(1);
    }

    [Given("un viaje activo de retorno")]
    public void GivenActiveReturnTrip()
    {
        _state.Trip = CreateStartedReturnTrip();
        _state.CurrentStudent = Valeria;
    }

    [Given("el alumno se encuentra en estado Boarded")]
    public void GivenStudentIsBoarded()
    {
        _state.Trip.RecordBoarding(
            Driver,
            _state.CurrentStudent,
            ClientEventId.New(),
            null,
            null,
            At(6, 41));

        _state.Trip.Passengers
            .Single(p => p.StudentId == _state.CurrentStudent)
            .Status.ShouldBe(PassengerStatus.Boarded);
    }

    [When("el conductor registra el desembarque")]
    public void WhenDriverRecordsAlighting()
    {
        _state.ActionTime = At(7, 10);

        _state.OperationApplied = _state.Trip.RecordAlighting(
            Driver,
            _state.CurrentStudent,
            ClientEventId.New(),
            _state.ActionTime,
            null,
            _state.ActionTime);
    }

    [Then("el estado del alumno debe cambiar a Alighted")]
    public void ThenStudentStatusIsAlighted()
    {
        _state.Trip.Passengers
            .Single(p => p.StudentId == _state.CurrentStudent)
            .Status.ShouldBe(PassengerStatus.Alighted);
    }

    [Then("debe generarse un evento de dominio StudentAlighted")]
    public void ThenStudentAlightedEventIsRaised()
    {
        _state.Trip.DomainEvents
            .OfType<StudentAlighted>()
            .ShouldHaveSingleItem();
    }
}