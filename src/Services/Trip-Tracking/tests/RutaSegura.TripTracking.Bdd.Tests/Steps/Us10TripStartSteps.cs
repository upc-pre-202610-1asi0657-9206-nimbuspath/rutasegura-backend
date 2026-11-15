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
public sealed class Us10TripStartSteps
{
    private readonly TripScenarioState _state;

    public Us10TripStartSteps(TripScenarioState state)
    {
        _state = state;
    }

    [Given("un viaje programado asignado al conductor")]
    [Given("un viaje programado asignado a un conductor")]
    public void GivenScheduledTripAssignedToDriver()
    {
        _state.Trip = CreateScheduledTrip();
    }

    [Given("el inicio se encuentra dentro de la ventana permitida")]
    public void GivenStartIsWithinAllowedWindow()
    {
        _state.ActionTime = At(6, 41);
    }

    [When("el conductor inicia el viaje")]
    public void WhenDriverStartsTrip()
    {
        _state.ClientEventId = ClientEventId.New();

        _state.OperationApplied = _state.Trip.Start(
            Driver,
            _state.ClientEventId,
            _state.ActionTime);
    }

    [Then("la operación debe aplicarse correctamente")]
    public void ThenOperationIsApplied()
    {
        _state.OperationApplied.ShouldBe(true);
    }

    [Then("el estado del viaje debe cambiar a InProgress")]
    public void ThenTripStatusIsInProgress()
    {
        _state.Trip.Status.ShouldBe(TripStatus.InProgress);
    }

    [Then("debe registrarse la hora de inicio")]
    public void ThenStartedAtIsRecorded()
    {
        _state.Trip.StartedAt.ShouldBe(_state.ActionTime);
    }

    [Then("debe generarse un evento TripStarted")]
    public void ThenTripStartedEventIsRaised()
    {
        _state.Trip.DomainEvents
            .OfType<TripStarted>()
            .ShouldHaveSingleItem();
    }

    [When("otro conductor intenta iniciar el viaje")]
    public void WhenAnotherDriverAttemptsToStartTrip()
    {
        try
        {
            _state.Trip.Start(
                OtherDriver,
                ClientEventId.New(),
                At(6, 40));
        }
        catch (DomainException ex)
        {
            _state.ErrorCode = ex.Code;
        }
    }

    [Then("la acción debe rechazarse con DriverMismatch")]
    public void ThenActionIsRejectedWithDriverMismatch()
    {
        _state.ErrorCode.ShouldBe(TripErrors.DriverMismatch);
    }

    [Then("el estado del viaje debe permanecer Scheduled")]
    public void ThenTripRemainsScheduled()
    {
        _state.Trip.Status.ShouldBe(TripStatus.Scheduled);
    }

    [When("el conductor intenta iniciar el viaje más de una hora antes")]
    public void WhenDriverAttemptsToStartTooEarly()
    {
        try
        {
            _state.Trip.Start(
                Driver,
                ClientEventId.New(),
                At(5, 39));
        }
        catch (DomainException ex)
        {
            _state.ErrorCode = ex.Code;
        }
    }

    [Then("la acción debe rechazarse con StartTooEarly")]
    public void ThenActionIsRejectedWithStartTooEarly()
    {
        _state.ErrorCode.ShouldBe(TripErrors.StartTooEarly);
    }

    [When("el conductor inicia el viaje más de diez minutos después de la hora programada")]
    public void WhenDriverStartsTripLate()
    {
        _state.ActionTime = At(6, 55);

        _state.OperationApplied = _state.Trip.Start(
            Driver,
            ClientEventId.New(),
            _state.ActionTime);
    }

    [Then("el estado del viaje debe cambiar a Delayed")]
    public void ThenTripStatusIsDelayed()
    {
        _state.Trip.Status.ShouldBe(TripStatus.Delayed);
    }

    [Then("debe generarse un evento TripDelayed")]
    public void ThenTripDelayedEventIsRaised()
    {
        _state.Trip.DomainEvents
            .OfType<TripDelayed>()
            .ShouldHaveSingleItem();
    }

    [Given("el viaje ya fue iniciado con un clientEventId")]
    public void GivenTripWasAlreadyStartedWithClientEventId()
    {
        _state.ClientEventId = ClientEventId.New();

        _state.Trip.Start(
            Driver,
            _state.ClientEventId,
            At(6, 40));

        _state.RepeatOperation = () =>
            _state.Trip.Start(
                Driver,
                _state.ClientEventId,
                At(6, 41));
    }

    [When("se procesa nuevamente el mismo clientEventId")]
    public void WhenSameClientEventIdIsProcessedAgain()
    {
        _state.RepeatOperation.ShouldNotBeNull();
        _state.OperationApplied = _state.RepeatOperation();
    }

    [Then("la segunda operación no debe aplicarse")]
    public void ThenSecondOperationIsNotApplied()
    {
        _state.OperationApplied.ShouldBe(false);
    }

    [Then("debe existir un solo evento TripStarted")]
    public void ThenOnlyOneTripStartedEventExists()
    {
        _state.Trip.DomainEvents
            .OfType<TripStarted>()
            .Count()
            .ShouldBe(1);
    }

    [Given("un viaje activo asignado al conductor")]
    [Given("un viaje activo")]
    public void GivenActiveTrip()
    {
        _state.Trip = CreateStartedTrip();
    }

    [Given("existe una lectura GPS válida")]
    public void GivenValidGpsReading()
    {
        _state.CurrentReading = CreateReading(
            FarSouth,
            At(6, 41),
            speedKmh: 28);

        _state.ReceivedAt = At(6, 41, 2);
    }

    [When("el servidor procesa la posición recibida")]
    public void WhenServerProcessesPosition()
    {
        _state.PositionResult = _state.Trip.RecordPosition(
            Driver,
            _state.CurrentReading!,
            _state.ReceivedAt);
    }

    [Then("la lectura debe ser aceptada")]
    public void ThenReadingIsAccepted()
    {
        _state.PositionResult!.Outcome
            .ShouldBe(PositionRecordingOutcome.Accepted);
    }

    [Then("debe actualizarse la última posición del viaje")]
    public void ThenLastPositionIsUpdated()
    {
        _state.Trip.LastPosition
            .ShouldBe(_state.CurrentReading!.Position);

        _state.Trip.LastPositionAt
            .ShouldBe(_state.CurrentReading.RecordedAt);
    }

    [Then("debe conservarse la fecha de recepción")]
    public void ThenReceivedAtIsPreserved()
    {
        _state.PositionResult!.Point!
            .ReceivedAt.ShouldBe(_state.ReceivedAt);
    }

    [Then("debe conservarse la velocidad registrada")]
    public void ThenSpeedIsPreserved()
    {
        _state.Trip.LastSpeedKmh
            .ShouldBe(_state.CurrentReading!.SpeedKmh);
    }

    [Given("un viaje activo con una posición previamente aceptada")]
    public void GivenActiveTripWithAcceptedPosition()
    {
        _state.Trip = CreateStartedTrip();

        var firstReading = CreateReading(
            FarSouth,
            At(6, 45));

        _state.Trip.RecordPosition(
            Driver,
            firstReading,
            At(6, 45));

        _state.PreviousPosition = _state.Trip.LastPosition;
        _state.PreviousPositionAt = _state.Trip.LastPositionAt;
    }

    [When("el servidor recibe una lectura con la misma fecha o una fecha anterior")]
    public void WhenServerReceivesStaleReading()
    {
        var staleReading = CreateReading(
            FarSouth + 0.001,
            At(6, 45));

        _state.PositionResult = _state.Trip.RecordPosition(
            Driver,
            staleReading,
            At(6, 46));
    }

    [Then("la lectura debe ignorarse como StaleOrDuplicate")]
    public void ThenReadingIsIgnoredAsStaleOrDuplicate()
    {
        _state.PositionResult!.Outcome
            .ShouldBe(PositionRecordingOutcome.IgnoredStaleOrDuplicate);
    }

    [Then("la última posición aceptada no debe ser reemplazada")]
    public void ThenLastPositionIsNotReplaced()
    {
        _state.Trip.LastPosition
            .ShouldBe(_state.PreviousPosition);

        _state.Trip.LastPositionAt
            .ShouldBe(_state.PreviousPositionAt);
    }

    [When("el servidor recibe una lectura GPS con baja precisión")]
    public void WhenServerReceivesLowAccuracyReading()
    {
        _state.CurrentReading = CreateReading(
            FarSouth,
            At(6, 41),
            accuracy: 150);

        _state.PositionResult = _state.Trip.RecordPosition(
            Driver,
            _state.CurrentReading,
            At(6, 41));
    }

    [Then("la lectura debe ignorarse como LowAccuracy")]
    public void ThenReadingIsIgnoredAsLowAccuracy()
    {
        _state.PositionResult!.Outcome
            .ShouldBe(PositionRecordingOutcome.IgnoredLowAccuracy);
    }

    [Then("no debe actualizarse la última posición del viaje")]
    public void ThenLastPositionIsNotUpdated()
    {
        _state.Trip.LastPosition.ShouldBeNull();
    }

    [Given("un viaje que todavía se encuentra Scheduled")]
    public void GivenTripIsStillScheduled()
    {
        _state.Trip = CreateScheduledTrip();
    }

    [When("el conductor intenta registrar una posición GPS")]
    public void WhenDriverAttemptsToRecordGpsPosition()
    {
        try
        {
            _state.Trip.RecordPosition(
                Driver,
                CreateReading(FarSouth, At(6, 30)),
                At(6, 30));
        }
        catch (DomainException ex)
        {
            _state.ErrorCode = ex.Code;
        }
    }

    [Then("la acción debe rechazarse con RequiresActiveTrip")]
    public void ThenActionIsRejectedWithRequiresActiveTrip()
    {
        _state.ErrorCode.ShouldBe(TrackingErrors.RequiresActiveTrip);
    }
}