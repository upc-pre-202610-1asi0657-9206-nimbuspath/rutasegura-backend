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
public sealed class Us13SosSteps
{
    private readonly TripScenarioState _state;
    private Guid _operatorId;

    public Us13SosSteps(TripScenarioState state)
    {
        _state = state;
    }

    [Given("existen estudiantes a bordo")]
    public void GivenStudentsAreOnBoard()
    {
        _state.Trip.RecordBoarding(
            Driver,
            Valeria,
            ClientEventId.New(),
            null,
            null,
            At(6, 58));

        _state.Trip.RecordBoarding(
            Driver,
            Mateo,
            ClientEventId.New(),
            null,
            null,
            At(6, 58));
    }

    [Given("existe una posición GPS disponible")]
    public void GivenGpsPositionIsAvailable()
    {
        _state.EventPosition =
            GeoPosition.Create(-12.095, -77.0000);
    }

    [When("el conductor activa una alerta SOS")]
    public void WhenDriverRaisesSos()
    {
        _state.OperationApplied = _state.Trip.RaiseSos(
            Driver,
            ClientEventId.New(),
            At(7, 5),
            _state.EventPosition,
            At(7, 5));
    }

    [Then("debe crearse una alerta SOS activa")]
    public void ThenActiveSosIsCreated()
    {
        _state.Trip.ActiveSos.ShouldNotBeNull();
    }

    [Then("debe generarse un evento SosRaised")]
    public void ThenSosRaisedEventIsGenerated()
    {
        _state.Trip.DomainEvents
            .OfType<SosRaised>()
            .ShouldHaveSingleItem();
    }

    [Then("el evento debe contener la posición actual")]
    public void ThenEventContainsCurrentPosition()
    {
        _state.Trip.DomainEvents
            .OfType<SosRaised>()
            .ShouldHaveSingleItem()
            .Position.ShouldBe(_state.EventPosition);
    }

    [Then("debe contener los estudiantes a bordo")]
    public void ThenEventContainsStudentsOnBoard()
    {
        _state.Trip.DomainEvents
            .OfType<SosRaised>()
            .ShouldHaveSingleItem()
            .StudentsOnBoard
            .ShouldBe(
                new[] { Valeria, Mateo },
                ignoreOrder: true);
    }

    [Then("debe contener el identificador del vehículo")]
    public void ThenEventContainsVehicleId()
    {
        _state.Trip.DomainEvents
            .OfType<SosRaised>()
            .ShouldHaveSingleItem()
            .VehicleId.ShouldBe(Vehicle);
    }

    [Given("no existe una posición GPS disponible")]
    public void GivenGpsPositionIsNotAvailable()
    {
        _state.EventPosition = null;
        _state.Trip.LastPosition.ShouldBeNull();
    }

    [Then("la alerta SOS debe registrarse")]
    public void ThenSosIsRecorded()
    {
        _state.Trip.SosAlerts.ShouldHaveSingleItem();
        _state.Trip.ActiveSos.ShouldNotBeNull();
    }

    [Then("la posición del evento SosRaised debe quedar vacía")]
    public void ThenSosRaisedPositionIsEmpty()
    {
        _state.Trip.DomainEvents
            .OfType<SosRaised>()
            .ShouldHaveSingleItem()
            .Position.ShouldBeNull();
    }

    [Given("existe una alerta SOS activa")]
    public void GivenActiveSosExists()
    {
        _state.Trip.RaiseSos(
            Driver,
            ClientEventId.New(),
            null,
            null,
            At(7, 5));

        _state.Trip.ActiveSos.ShouldNotBeNull();
    }

    [When("el conductor intenta activar otra alerta SOS")]
    public void WhenDriverAttemptsAnotherSos()
    {
        _state.OperationApplied = _state.Trip.RaiseSos(
            Driver,
            ClientEventId.New(),
            null,
            null,
            At(7, 6));
    }

    [Then("debe permanecer una sola alerta SOS registrada")]
    public void ThenOnlyOneSosRemains()
    {
        _state.Trip.SosAlerts.Count.ShouldBe(1);
    }

    [Given("un viaje que todavía no ha iniciado")]
    public void GivenTripHasNotStarted()
    {
        _state.Trip = CreateScheduledTrip();
    }

    [When("el conductor intenta activar una alerta SOS")]
    public void WhenDriverAttemptsSosBeforeTripStarts()
    {
        try
        {
            _state.Trip.RaiseSos(
                Driver,
                ClientEventId.New(),
                null,
                null,
                At(6, 30));
        }
        catch (DomainException ex)
        {
            _state.ErrorCode = ex.Code;
        }
    }

    [Then("la acción debe rechazarse con NotActive")]
    public void ThenActionIsRejectedWithNotActive()
    {
        _state.ErrorCode.ShouldBe(TripErrors.NotActive);
    }

    [When("un operador autorizado resuelve la alerta")]
    public void WhenAuthorizedOperatorResolvesSos()
    {
        _operatorId = Guid.NewGuid();

        _state.Trip.ResolveSos(
            _operatorId,
            At(7, 20));
    }

    [Then("ya no debe existir una alerta SOS activa")]
    public void ThenThereIsNoActiveSos()
    {
        _state.Trip.ActiveSos.ShouldBeNull();
    }

    [Then("debe generarse un evento SosResolved")]
    public void ThenSosResolvedEventIsGenerated()
    {
        _state.Trip.DomainEvents
            .OfType<SosResolved>()
            .ShouldHaveSingleItem();
    }

    [Then("debe registrarse el operador que resolvió la alerta")]
    public void ThenResolvingOperatorIsRecorded()
    {
        _state.Trip.DomainEvents
            .OfType<SosResolved>()
            .ShouldHaveSingleItem()
            .ResolvedBy.ShouldBe(_operatorId);
    }

    [Given("un viaje activo sin una alerta SOS activa")]
    public void GivenActiveTripWithoutSos()
    {
        _state.Trip = CreateStartedTrip();
        _state.Trip.ActiveSos.ShouldBeNull();
    }

    [When("un operador intenta resolver una alerta SOS")]
    public void WhenOperatorAttemptsToResolveMissingSos()
    {
        try
        {
            _state.Trip.ResolveSos(
                Guid.NewGuid(),
                At(7, 0));
        }
        catch (DomainException ex)
        {
            _state.ErrorCode = ex.Code;
        }
    }

    [Then("la acción debe rechazarse con NoActiveSos")]
    public void ThenActionIsRejectedWithNoActiveSos()
    {
        _state.ErrorCode.ShouldBe(TripErrors.NoActiveSos);
    }
}