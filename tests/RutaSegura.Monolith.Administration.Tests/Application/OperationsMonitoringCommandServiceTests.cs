using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Commands;
using RutaSegura.Monolith.Administration.Tests.Support;
using static RutaSegura.Monolith.Administration.Tests.Support.AdministrationMother;

namespace RutaSegura.Monolith.Administration.Tests.Application;

/// <summary>What Administration does with IncidentReported, SosRaised/Resolved and TripCompleted.</summary>
public sealed class OperationsMonitoringCommandServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly AdministrationFixture _fixture = new();
    private readonly Guid _tripId = Guid.NewGuid();
    private readonly Guid _routeId = Guid.NewGuid();

    private RecordIncidentReportedCommand Incident(Guid? incidentId = null, Guid? organizationId = null,
        string type = "VehicleFailure") => new(
        _tripId, _routeId, organizationId ?? _fixture.OrganizationId, "Ruta 3", incidentId ?? Guid.NewGuid(), type,
        "Llanta baja", -12.1100, -76.9850, 4, Now);

    private RecordSosRaisedCommand Sos(double? latitude = -12.11, double? longitude = -76.98) => new(
        _tripId, _routeId, _fixture.OrganizationId, "Ruta 3", Guid.NewGuid(), Guid.NewGuid(), latitude, longitude, 6,
        Now);

    [Fact]
    public async Task IncidentReported_CreatesAnOpenAlertWithTheSeverityOfItsType()
    {
        var result = await _fixture.Monitoring.Handle(Incident(type: "VehicleFailure"), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        var alert = _fixture.Alerts.Items.ShouldHaveSingleItem();
        alert.Severity.Value.ShouldBe("HIGH");
        alert.Category!.Value.ShouldBe("VEHICLE_FAILURE");
        alert.Location.ShouldNotBeNull();
        alert.AffectedStudents.ShouldBe(4);
        alert.OrganizationId.ShouldBe(Organization);
    }

    [Fact]
    public async Task IncidentReported_SameIncidentDeliveredTwice_IsRecordedOnce()
    {
        var incidentId = Guid.NewGuid();

        var first = await _fixture.Monitoring.Handle(Incident(incidentId), Ct);
        var second = await _fixture.Monitoring.Handle(Incident(incidentId), Ct);

        second.Value.ShouldBeSameAs(first.Value);
        _fixture.Alerts.Items.Count.ShouldBe(1);
        _fixture.UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task IncidentReported_WithoutOrganization_UsesTheOrganizationOfThePlannedRoute()
    {
        var route = _fixture.AddRoute(NewRoute());
        var command = Incident(organizationId: Guid.Empty) with { RouteId = route.Id.Identifier, RouteName = null };

        var result = await _fixture.Monitoring.Handle(command, Ct);

        result.Value!.OrganizationId.ShouldBe(route.OrganizationId);
        result.Value.RouteName.ShouldBe(route.Name.Value);
    }

    [Fact]
    public async Task IncidentReported_UnknownRouteAndNoOrganization_FailsWithRouteNotFound() =>
        (await _fixture.Monitoring.Handle(Incident(organizationId: Guid.Empty), Ct))
        .Error.ShouldBe(AdministrationError.RouteNotFound);

    [Fact]
    public async Task SosRaised_WithAnInvalidGpsFix_IsStillRecordedAsCritical()
    {
        var result = await _fixture.Monitoring.Handle(Sos(latitude: 0, longitude: 0), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        result.Value!.Severity.IsCritical().ShouldBeTrue();
        result.Value.Location.ShouldBeNull();
        result.Value.AffectedStudents.ShouldBe(6);
    }

    [Fact]
    public async Task SosRaised_DeliveredTwiceWhileOpen_KeepsASingleAlert()
    {
        await _fixture.Monitoring.Handle(Sos(), Ct);
        await _fixture.Monitoring.Handle(Sos(), Ct);

        _fixture.Alerts.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SosResolved_ClosesTheSosOfTheTripAndIsIdempotent()
    {
        await _fixture.Monitoring.Handle(Sos(), Ct);
        var resolvedBy = Guid.NewGuid();
        var resolved = new RecordSosResolvedCommand(_tripId, _routeId, _fixture.OrganizationId, resolvedBy,
            Now.AddMinutes(15));

        var first = await _fixture.Monitoring.Handle(resolved, Ct);
        var second = await _fixture.Monitoring.Handle(resolved, Ct);

        first.IsSuccess.ShouldBeTrue(first.Message);
        second.IsSuccess.ShouldBeTrue(second.Message);
        first.Value!.IsOpen().ShouldBeFalse();
        first.Value.ResolvedBy.ShouldBe(resolvedBy);
        _fixture.UnitOfWork.Commits.ShouldBe(2); // raise + first resolution
    }

    [Fact]
    public async Task SosResolved_WithoutARecordedSos_FailsWithAlertNotFound() =>
        (await _fixture.Monitoring.Handle(
            new RecordSosResolvedCommand(_tripId, _routeId, _fixture.OrganizationId, Guid.NewGuid(), Now), Ct))
        .Error.ShouldBe(AdministrationError.AlertNotFound);

    [Fact]
    public async Task TripCompleted_CreatesATripRecordOnce()
    {
        var command = new RecordTripCompletedCommand(_tripId, _routeId, _fixture.OrganizationId, "Ruta 3", 9, 1, 2,
            Now, Now.AddMinutes(58));

        var first = await _fixture.Monitoring.Handle(command, Ct);
        var second = await _fixture.Monitoring.Handle(command, Ct);

        first.IsSuccess.ShouldBeTrue(first.Message);
        second.Value.ShouldBeSameAs(first.Value);
        var tripRecord = _fixture.TripRecords.Items.ShouldHaveSingleItem();
        tripRecord.DeliveredStudents.ShouldBe(9);
        tripRecord.IncidentCount.ShouldBe(2);
        tripRecord.Duration.ShouldBe(TimeSpan.FromMinutes(58));
    }

    [Fact]
    public async Task AcknowledgeAlert_OfAnotherOrganization_FailsAsNotFound()
    {
        var alert = (await _fixture.Monitoring.Handle(Incident(), Ct)).Value!;

        var result = await _fixture.Monitoring.Handle(
            new AcknowledgeAlertCommand(OtherOrganization.Identifier, alert.Id.Identifier, Guid.NewGuid()), Ct);

        result.Error.ShouldBe(AdministrationError.AlertNotFound);
        alert.Status.IsOpen().ShouldBeTrue();
    }

    [Fact]
    public async Task ResolveAlert_Sos_FailsBecauseItIsResolvedInTripTracking()
    {
        var alert = (await _fixture.Monitoring.Handle(Sos(), Ct)).Value!;

        var result = await _fixture.Monitoring.Handle(
            new ResolveAlertCommand(_fixture.OrganizationId, alert.Id.Identifier, Guid.NewGuid()), Ct);

        result.Error.ShouldBe(AdministrationError.AlertInvalidTransition);
    }

    [Fact]
    public async Task AcknowledgeThenResolve_Incident_ClosesTheFollowUp()
    {
        var alert = (await _fixture.Monitoring.Handle(Incident(), Ct)).Value!;
        var administrator = Guid.NewGuid();

        await _fixture.Monitoring.Handle(new AcknowledgeAlertCommand(_fixture.OrganizationId, alert.Id.Identifier,
            administrator), Ct);
        var result = await _fixture.Monitoring.Handle(new ResolveAlertCommand(_fixture.OrganizationId,
            alert.Id.Identifier, administrator), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        alert.IsOpen().ShouldBeFalse();
        alert.AcknowledgedAt.ShouldBe(Now);
    }
}
