using Reqnroll;
using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Bdd.Tests.Support;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Domain.Trips.Events;
using static RutaSegura.TripTracking.Bdd.Tests.Support.TripScenarioState;

namespace RutaSegura.TripTracking.Bdd.Tests.Steps;

[Binding]
public sealed class Us12IncidentSteps
{
    private readonly TripScenarioState _state;

    public Us12IncidentSteps(TripScenarioState state)
    {
        _state = state;
    }

    [When("el conductor reporta una incidencia HeavyTraffic")]
    public void WhenDriverReportsHeavyTraffic()
    {
        _state.ClientEventId = ClientEventId.New();
        _state.ActionTime = At(7, 12);
    }

    [When("solicita notificar a los tutores")]
    public void WhenDriverRequestsGuardianNotification()
    {
        _state.OperationApplied = _state.Trip.ReportIncident(
            Driver,
            IncidentType.HeavyTraffic,
            null,
            notifyGuardians: true,
            _state.ClientEventId,
            _state.ActionTime,
            null,
            _state.ActionTime);
    }

    [Then("la incidencia debe quedar registrada")]
    public void ThenIncidentIsRecorded()
    {
        _state.Trip.Incidents.ShouldHaveSingleItem();
    }

    [Then("debe generarse un evento IncidentReported")]
    public void ThenIncidentReportedEventIsRaised()
    {
        _state.Trip.DomainEvents
            .OfType<IncidentReported>()
            .ShouldHaveSingleItem();
    }

    [When("el conductor reporta una incidencia StudentUnwell")]
    public void WhenDriverReportsStudentUnwell()
    {
        _state.OperationApplied = _state.Trip.ReportIncident(
            Driver,
            IncidentType.StudentUnwell,
            "Mateo con náuseas",
            notifyGuardians: false,
            ClientEventId.New(),
            null,
            null,
            At(7, 0));
    }

    [Then("el estado del viaje debe permanecer InProgress")]
    public void ThenTripRemainsInProgress()
    {
        _state.Trip.Status.ShouldBe(TripStatus.InProgress);
    }

    [Then("no debe generarse un evento TripDelayed")]
    public void ThenTripDelayedEventIsNotRaised()
    {
        _state.Trip.DomainEvents
            .OfType<TripDelayed>()
            .ShouldBeEmpty();
    }

    [When("el conductor reporta una incidencia Other sin un detalle válido")]
    public void WhenDriverReportsOtherWithoutValidDetail()
    {
        try
        {
            _state.Trip.ReportIncident(
                Driver,
                IncidentType.Other,
                "   ",
                notifyGuardians: false,
                ClientEventId.New(),
                null,
                null,
                At(7, 0));
        }
        catch (DomainException ex)
        {
            _state.ErrorCode = ex.Code;
        }
    }

    [Then("la acción debe rechazarse con IncidentDetailRequired")]
    public void ThenActionIsRejectedWithIncidentDetailRequired()
    {
        _state.ErrorCode.ShouldBe(TripErrors.IncidentDetailRequired);
    }

    [When("el conductor reporta una incidencia con más de 500 caracteres de detalle")]
    public void WhenDriverReportsIncidentWithTooLongDetail()
    {
        try
        {
            _state.Trip.ReportIncident(
                Driver,
                IncidentType.Other,
                new string('x', 501),
                notifyGuardians: false,
                ClientEventId.New(),
                null,
                null,
                At(7, 0));
        }
        catch (DomainException ex)
        {
            _state.ErrorCode = ex.Code;
        }
    }

    [Then("la acción debe rechazarse con IncidentDetailTooLong")]
    public void ThenActionIsRejectedWithIncidentDetailTooLong()
    {
        _state.ErrorCode.ShouldBe(TripErrors.IncidentDetailTooLong);
    }

    [Given("una incidencia fue registrada con un clientEventId")]
    public void GivenIncidentWasRecordedWithClientEventId()
    {
        _state.ClientEventId = ClientEventId.New();

        _state.Trip.ReportIncident(
            Driver,
            IncidentType.HeavyTraffic,
            null,
            notifyGuardians: true,
            _state.ClientEventId,
            null,
            null,
            At(7, 0));

        _state.RepeatOperation = () =>
            _state.Trip.ReportIncident(
                Driver,
                IncidentType.HeavyTraffic,
                null,
                notifyGuardians: true,
                _state.ClientEventId,
                null,
                null,
                At(7, 1));
    }

    [Then("la acción no debe aplicarse nuevamente")]
    public void ThenActionIsNotAppliedAgain()
    {
        _state.OperationApplied.ShouldBe(false);
    }

    [Then("debe existir una sola incidencia registrada")]
    public void ThenOnlyOneIncidentExists()
    {
        _state.Trip.Incidents.Count.ShouldBe(1);
    }
}