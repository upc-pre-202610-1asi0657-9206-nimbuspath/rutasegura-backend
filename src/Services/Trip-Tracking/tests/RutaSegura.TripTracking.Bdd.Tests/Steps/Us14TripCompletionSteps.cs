using Reqnroll;
using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Bdd.Tests.Support;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Domain.Trips.Events;
using static RutaSegura.TripTracking.Bdd.Tests.Support.TripScenarioState;

namespace RutaSegura.TripTracking.Bdd.Tests.Steps;

[Binding]
public sealed class Us14TripCompletionSteps
{
    private readonly TripScenarioState _state;

    public Us14TripCompletionSteps(TripScenarioState state)
    {
        _state = state;
    }

    [Given("un viaje activo de ida")]
    public void GivenActivePickupTrip()
    {
        _state.Trip = CreateStartedTrip();
    }

    [Given("todos los pasajeros fueron registrados como abordados o ausentes")]
    public void GivenAllPassengersWereBoardedOrAbsent()
    {
        _state.Trip.RecordBoarding(
            Driver,
            Valeria,
            ClientEventId.New(),
            At(6, 50),
            null,
            At(6, 50));

        _state.Trip.RecordBoarding(
            Driver,
            Mateo,
            ClientEventId.New(),
            At(6, 50),
            null,
            At(6, 50));

        _state.Trip.MarkStudentAbsent(
            Driver,
            Camila,
            ClientEventId.New(),
            At(7, 0),
            At(7, 0));
    }

    [Given("no existe una alerta SOS activa")]
    public void GivenThereIsNoActiveSos()
    {
        _state.Trip.ActiveSos.ShouldBeNull();
    }

    [When("el conductor finaliza el viaje")]
    public void WhenDriverCompletesTrip()
    {
        _state.OperationApplied = _state.Trip.Complete(
            Driver,
            ClientEventId.New(),
            At(7, 53));
    }

    [Then("el estado del viaje debe cambiar a Completed")]
    public void ThenTripStatusIsCompleted()
    {
        _state.Trip.Status.ShouldBe(TripStatus.Completed);
    }

    [Then("los estudiantes abordados deben quedar en estado Alighted")]
    public void ThenBoardedStudentsBecomeAlighted()
    {
        _state.Trip.Passengers
            .Single(p => p.StudentId == Valeria)
            .Status.ShouldBe(PassengerStatus.Alighted);

        _state.Trip.Passengers
            .Single(p => p.StudentId == Mateo)
            .Status.ShouldBe(PassengerStatus.Alighted);
    }

    [Then("debe generarse un evento TripCompleted")]
    public void ThenTripCompletedEventIsGenerated()
    {
        _state.Trip.DomainEvents
            .OfType<TripCompleted>()
            .ShouldHaveSingleItem();
    }

    [Then("el evento debe contener los estudiantes entregados")]
    public void ThenEventContainsDeliveredStudents()
    {
        var completed = _state.Trip.DomainEvents
            .OfType<TripCompleted>()
            .ShouldHaveSingleItem();

        completed.DeliveredStudents.ShouldBe(
            new[] { Valeria, Mateo },
            ignoreOrder: true);
    }

    [Then("debe contener los estudiantes ausentes")]
    public void ThenEventContainsAbsentStudents()
    {
        var completed = _state.Trip.DomainEvents
            .OfType<TripCompleted>()
            .ShouldHaveSingleItem();

        completed.AbsentStudents
            .ShouldHaveSingleItem()
            .ShouldBe(Camila);
    }

    [Then("debe contener la cantidad de incidencias registradas")]
    public void ThenEventContainsIncidentCount()
    {
        var completed = _state.Trip.DomainEvents
            .OfType<TripCompleted>()
            .ShouldHaveSingleItem();

        completed.IncidentCount.ShouldBe(_state.Trip.Incidents.Count);
    }

    [Given("existe al menos un pasajero en estado Pending")]
    public void GivenAtLeastOnePassengerIsPending()
    {
        _state.Trip.Passengers
            .Any(p => p.Status == PassengerStatus.Pending)
            .ShouldBeTrue();
    }

    [When("el conductor intenta finalizar el viaje")]
    public void WhenDriverAttemptsToCompleteTrip()
    {
        try
        {
            _state.Trip.Complete(
                Driver,
                ClientEventId.New(),
                At(7, 50));
        }
        catch (DomainException ex)
        {
            _state.ErrorCode = ex.Code;
        }
    }

    [Then("la acción debe rechazarse con HasPendingStudents")]
    public void ThenActionIsRejectedWithHasPendingStudents()
    {
        _state.ErrorCode.ShouldBe(TripErrors.HasPendingStudents);
    }

    [Given("existe al menos un estudiante en estado Boarded")]
    public void GivenAtLeastOneStudentIsBoarded()
    {
        foreach (var student in new[] { Valeria, Mateo, Camila })
        {
            _state.Trip.RecordBoarding(
                Driver,
                student,
                ClientEventId.New(),
                null,
                null,
                At(6, 41));
        }

        _state.Trip.Passengers
            .Any(p => p.Status == PassengerStatus.Boarded)
            .ShouldBeTrue();
    }

    [Then("la acción debe rechazarse con HasStudentsOnBoard")]
    public void ThenActionIsRejectedWithHasStudentsOnBoard()
    {
        _state.ErrorCode.ShouldBe(TripErrors.HasStudentsOnBoard);
    }

    [Given("todos los estudiantes abordaron y posteriormente desembarcaron")]
    public void GivenAllStudentsBoardedAndAlighted()
    {
        foreach (var student in new[] { Valeria, Mateo, Camila })
        {
            _state.Trip.RecordBoarding(
                Driver,
                student,
                ClientEventId.New(),
                null,
                null,
                At(6, 41));

            _state.Trip.RecordAlighting(
                Driver,
                student,
                ClientEventId.New(),
                null,
                null,
                At(7, 10));
        }
    }

    [Then("la acción debe rechazarse con HasActiveSos")]
    public void ThenActionIsRejectedWithHasActiveSos()
    {
        _state.ErrorCode.ShouldBe(TripErrors.HasActiveSos);
    }

    [Given("un viaje programado")]
    public void GivenScheduledTrip()
    {
        _state.Trip = CreateScheduledTrip();
    }

    [Then("la acción debe rechazarse con InvalidTransition")]
    public void ThenActionIsRejectedWithInvalidTransition()
    {
        _state.ErrorCode.ShouldBe(TripErrors.InvalidTransition);
    }
}