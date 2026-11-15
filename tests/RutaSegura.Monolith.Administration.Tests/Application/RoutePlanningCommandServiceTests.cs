using Monolith.Administration.Application.IntegrationEvents.Outbound;
using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Commands;
using Monolith.Administration.Domain.Model.Queries;
using RutaSegura.Monolith.Administration.Tests.Support;
using static RutaSegura.Monolith.Administration.Tests.Support.AdministrationMother;

namespace RutaSegura.Monolith.Administration.Tests.Application;

public sealed class RoutePlanningCommandServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly AdministrationFixture _fixture = new();

    private Guid Org => _fixture.OrganizationId;

    private CreateRouteCommand CreateRuta3(string name = "Ruta 3 - Colegio San Agustin") => new(
        Org, name, "PICKUP", new TimeOnly(6, 45), new TimeOnly(7, 50), "America/Lima", Weekdays);

    // ── Routes and stops (US-04) ─────────────────────────────────────────────

    [Fact]
    public async Task CreateRoute_ValidData_PersistsADraftAndPublishesNothing()
    {
        var result = await _fixture.RoutePlanning.Handle(CreateRuta3(), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        result.Value!.Status.IsDraft().ShouldBeTrue();
        _fixture.Routes.Items.ShouldHaveSingleItem();
        _fixture.Outbox.Events.ShouldBeEmpty();
        _fixture.UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task CreateRoute_NameAlreadyUsedInTheOrganization_FailsWithRouteNameAlreadyExists()
    {
        await _fixture.RoutePlanning.Handle(CreateRuta3(), Ct);

        var result = await _fixture.RoutePlanning.Handle(CreateRuta3("ruta 3 - colegio san agustin"), Ct);

        result.Error.ShouldBe(AdministrationError.RouteNameAlreadyExists);
    }

    [Fact]
    public async Task CreateRoute_UnknownDirection_FailsWithInvalidData() =>
        (await _fixture.RoutePlanning.Handle(CreateRuta3() with { Direction = "CIRCULAR" }, Ct))
        .Error.ShouldBe(AdministrationError.InvalidAdministrationData);

    [Fact]
    public async Task AddStopAndAssignStudent_DraftRoute_DefinesWhereTheChildIsPickedUp()
    {
        var route = (await _fixture.RoutePlanning.Handle(CreateRuta3(), Ct)).Value!;
        var mateo = Guid.NewGuid();

        var withStop = await _fixture.RoutePlanning.Handle(new AddRouteStopCommand(Org, route.Id.Identifier,
            "Av. Primavera 456", "Av. Primavera 456, Surco", -12.11234, -76.98567, null), Ct);
        var stopId = withStop.Value!.Stops[0].Id.Identifier;
        var withStudent = await _fixture.RoutePlanning.Handle(
            new AssignStudentToStopCommand(Org, route.Id.Identifier, stopId, mateo), Ct);

        withStudent.IsSuccess.ShouldBeTrue(withStudent.Message);
        route.Stops[0].Students.ShouldHaveSingleItem().Identifier.ShouldBe(mateo);
        route.Direction.StopPurpose.ShouldBe("PICK_UP");
    }

    [Fact]
    public async Task AssignStudent_BeyondTheAssignedVehicleCapacity_FailsWithCapacityExceeded()
    {
        var route = _fixture.AddReadyRoute(_fixture.AddDriver(), _fixture.AddVehicle(capacity: 2), students: 2);

        var result = await _fixture.RoutePlanning.Handle(new AssignStudentToStopCommand(Org, route.Id.Identifier,
            route.Stops[0].Id.Identifier, Guid.NewGuid()), Ct);

        result.Error.ShouldBe(AdministrationError.VehicleCapacityExceeded);
        route.StudentCount.ShouldBe(2);
    }

    [Fact]
    public async Task AnyChange_RouteOfAnotherOrganization_FailsAsNotFound()
    {
        var foreign = _fixture.AddRoute(NewRoute(organization: OtherOrganization));

        var result = await _fixture.RoutePlanning.Handle(new RenameRouteCommand(Org, foreign.Id.Identifier, "Mía"), Ct);

        result.Error.ShouldBe(AdministrationError.RouteNotFound);
    }

    // ── Assignment (US-05) ───────────────────────────────────────────────────

    [Fact]
    public async Task AssignResources_DraftRoute_EnqueuesRouteAssignedWithTheDriverIamAccount()
    {
        var driver = _fixture.AddDriver();
        var vehicle = _fixture.AddVehicle();
        var route = _fixture.AddRoute(NewRoute());

        var result = await _fixture.RoutePlanning.Handle(
            new AssignRouteResourcesCommand(Org, route.Id.Identifier, driver.Id.Identifier, vehicle.Id.Identifier), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        var message = _fixture.Outbox.Events.ShouldHaveSingleItem();
        message.MessageType.ShouldBe(AdministrationMessageTypes.RouteAssigned);
        var payload = message.Payload.ShouldBeOfType<RouteAssignedV1>();
        payload.DriverId.ShouldBe(driver.UserId.Identifier);
        payload.VehicleId.ShouldBe(vehicle.Id.Identifier);
        payload.RouteIsActive.ShouldBeFalse();
        _fixture.UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task AssignResources_DriverBusyOnAnOverlappingRoute_FailsWithScheduleConflictAndSavesNothing()
    {
        var driver = _fixture.AddDriver();
        _fixture.AddActiveRoute(driver, _fixture.AddVehicle(), name: "Ruta 1"); // 06:45 - 07:50
        var route = _fixture.AddRoute(NewRoute("Ruta 2", Schedule(new TimeOnly(7, 30), new TimeOnly(8, 15))));

        var result = await _fixture.RoutePlanning.Handle(new AssignRouteResourcesCommand(Org, route.Id.Identifier,
            driver.Id.Identifier, _fixture.AddVehicle().Id.Identifier), Ct);

        result.Error.ShouldBe(AdministrationError.ResourceScheduleConflict);
        route.Assignment.ShouldBeNull();
        _fixture.Outbox.Events.ShouldBeEmpty();
        _fixture.UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task AssignResources_DriverOfAnotherOrganization_FailsAsNotFound()
    {
        var route = _fixture.AddRoute(NewRoute());
        var foreign = _fixture.AddDriver(OtherOrganization);

        var result = await _fixture.RoutePlanning.Handle(new AssignRouteResourcesCommand(Org, route.Id.Identifier,
            foreign.Id.Identifier, _fixture.AddVehicle().Id.Identifier), Ct);

        result.Error.ShouldBe(AdministrationError.DriverNotFound);
    }

    [Fact]
    public async Task AssignResources_ActiveRoute_EnqueuesRouteAssignedAndRouteUpdated()
    {
        var route = _fixture.AddActiveRoute(_fixture.AddDriver(), _fixture.AddVehicle());
        var replacement = _fixture.AddDriver();

        await _fixture.RoutePlanning.Handle(new AssignRouteResourcesCommand(Org, route.Id.Identifier,
            replacement.Id.Identifier, route.Assignment!.VehicleId.Identifier), Ct);

        _fixture.Outbox.MessageTypes.ShouldBe([AdministrationMessageTypes.RouteAssigned, AdministrationMessageTypes.RouteUpdated], ignoreOrder: false);
        var snapshot = _fixture.Outbox.Events[1].Payload.ShouldBeOfType<RoutePublishedV1>();
        snapshot.DriverId.ShouldBe(replacement.UserId.Identifier);
    }

    [Fact]
    public async Task RescheduleRoute_IntoAnotherRouteOfTheSameVehicle_FailsWithScheduleConflict()
    {
        var vehicle = _fixture.AddVehicle();
        _fixture.AddActiveRoute(_fixture.AddDriver(), vehicle, name: "Ruta 1"); // 06:45 - 07:50
        var afternoon = Schedule(new TimeOnly(13, 0), new TimeOnly(14, 0));
        var route = _fixture.AddReadyRoute(_fixture.AddDriver(), vehicle, schedule: afternoon, name: "Ruta 2");

        var result = await _fixture.RoutePlanning.Handle(new RescheduleRouteCommand(Org, route.Id.Identifier,
            new TimeOnly(7, 0), new TimeOnly(8, 0), "America/Lima", Weekdays), Ct);

        result.Error.ShouldBe(AdministrationError.ResourceScheduleConflict);
        route.Schedule.ShouldBe(afternoon);
    }

    // ── Activation, plan limit and messages for Trip & Tracking ──────────────

    [Fact]
    public async Task ActivateRoute_Ready_EnqueuesRoutePublishedSnapshotForTripTracking()
    {
        var driver = _fixture.AddDriver();
        var vehicle = _fixture.AddVehicle();
        var route = _fixture.AddReadyRoute(driver, vehicle, students: 3);

        var result = await _fixture.RoutePlanning.Handle(new ActivateRouteCommand(Org, route.Id.Identifier), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        route.IsActive().ShouldBeTrue();
        var message = _fixture.Outbox.Events.ShouldHaveSingleItem();
        message.MessageType.ShouldBe("administration.route-published");
        message.SchemaVersion.ShouldBe(1);

        var snapshot = message.Payload.ShouldBeOfType<RoutePublishedV1>();
        snapshot.RouteId.ShouldBe(route.Id.Identifier);
        snapshot.OrganizationId.ShouldBe(Org);
        snapshot.DriverId.ShouldBe(driver.UserId.Identifier); // IAM account, the id the mobile app signs in with
        snapshot.VehicleId.ShouldBe(vehicle.Id.Identifier);
        snapshot.Direction.ShouldBe("Pickup");
        snapshot.DepartureTime.ShouldBe("06:45");
        snapshot.TimeZoneId.ShouldBe("America/Lima");
        snapshot.OperatingDays.ShouldBe(["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"], ignoreOrder: false);
        snapshot.Stops.Select(s => s.Sequence).ShouldBe([1, 2], ignoreOrder: false);
        snapshot.Students.Count.ShouldBe(3);
        snapshot.Students.ShouldAllBe(s => snapshot.Stops.Any(stop => stop.StopId == s.StopId));
    }

    [Fact]
    public async Task ActivateRoute_PlanLimitReached_FailsAndPublishesNothing()
    {
        _fixture.PlanLimits.Limits = Plan(maxActiveRoutes: 1);
        _fixture.AddActiveRoute(_fixture.AddDriver(), _fixture.AddVehicle(), name: "Ruta 1");
        var route = _fixture.AddReadyRoute(_fixture.AddDriver(), _fixture.AddVehicle(), name: "Ruta 2");

        var result = await _fixture.RoutePlanning.Handle(new ActivateRouteCommand(Org, route.Id.Identifier), Ct);

        result.Error.ShouldBe(AdministrationError.PlanRouteLimitReached);
        route.Status.IsDraft().ShouldBeTrue();
        _fixture.Outbox.Events.ShouldBeEmpty();
        _fixture.UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task ActivateRoute_InactiveRoutesDoNotCountAgainstThePlan()
    {
        _fixture.PlanLimits.Limits = Plan(maxActiveRoutes: 1);
        var retired = _fixture.AddActiveRoute(_fixture.AddDriver(), _fixture.AddVehicle(), name: "Ruta 1");
        retired.Deactivate();
        var route = _fixture.AddReadyRoute(_fixture.AddDriver(), _fixture.AddVehicle(), name: "Ruta 2");

        var result = await _fixture.RoutePlanning.Handle(new ActivateRouteCommand(Org, route.Id.Identifier), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task ActivateRoute_WithoutAssignment_FailsWithRouteNotReady()
    {
        var route = _fixture.AddRoute(NewRoute());

        (await _fixture.RoutePlanning.Handle(new ActivateRouteCommand(Org, route.Id.Identifier), Ct))
            .Error.ShouldBe(AdministrationError.RouteNotReadyForActivation);
    }

    [Fact]
    public async Task ActivateRoute_ReactivationWhenTheDriverWasTakenMeanwhile_FailsWithScheduleConflict()
    {
        var driver = _fixture.AddDriver();
        var first = _fixture.AddActiveRoute(driver, _fixture.AddVehicle(), name: "Ruta 1");
        first.Deactivate();
        _fixture.AddActiveRoute(driver, _fixture.AddVehicle(), name: "Ruta 2"); // same time window

        var result = await _fixture.RoutePlanning.Handle(new ActivateRouteCommand(Org, first.Id.Identifier), Ct);

        result.Error.ShouldBe(AdministrationError.ResourceScheduleConflict);
        first.Status.IsInactive().ShouldBeTrue();
    }

    [Fact]
    public async Task ChangeOnActiveRoute_EnqueuesASingleRouteUpdated()
    {
        var route = _fixture.AddActiveRoute(_fixture.AddDriver(), _fixture.AddVehicle());

        await _fixture.RoutePlanning.Handle(new AddRouteStopCommand(Org, route.Id.Identifier, "Parque Kennedy", null,
            -12.1211, -77.0297, Position: 1), Ct);

        var message = _fixture.Outbox.Events.ShouldHaveSingleItem();
        message.MessageType.ShouldBe(AdministrationMessageTypes.RouteUpdated);
        message.Payload.ShouldBeOfType<RoutePublishedV1>().Stops.First().Name.ShouldBe("Parque Kennedy");
    }

    [Fact]
    public async Task DeactivateRoute_EnqueuesRouteRetired()
    {
        var route = _fixture.AddActiveRoute(_fixture.AddDriver(), _fixture.AddVehicle());

        await _fixture.RoutePlanning.Handle(new DeactivateRouteCommand(Org, route.Id.Identifier), Ct);

        var message = _fixture.Outbox.Events.ShouldHaveSingleItem();
        message.MessageType.ShouldBe(AdministrationMessageTypes.RouteRetired);
        message.Payload.ShouldBeOfType<RouteRetiredV1>().RouteId.ShouldBe(route.Id.Identifier);
    }

    [Fact]
    public async Task DeleteRoute_ActiveRoute_FailsAndDraftRouteIsRemoved()
    {
        var active = _fixture.AddActiveRoute(_fixture.AddDriver(), _fixture.AddVehicle(), name: "Ruta 1");
        var draft = _fixture.AddRoute(NewRoute("Ruta 2"));

        (await _fixture.RoutePlanning.Handle(new DeleteRouteCommand(Org, active.Id.Identifier), Ct))
            .Error.ShouldBe(AdministrationError.RouteInvalidTransition);
        (await _fixture.RoutePlanning.Handle(new DeleteRouteCommand(Org, draft.Id.Identifier), Ct))
            .IsSuccess.ShouldBeTrue();

        _fixture.Routes.Items.ShouldHaveSingleItem().ShouldBe(active);
    }

    // ── Quota query ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveRouteQuota_ReportsActiveRoutesAgainstThePlan()
    {
        _fixture.PlanLimits.Limits = Plan(maxActiveRoutes: 2);
        _fixture.AddActiveRoute(_fixture.AddDriver(), _fixture.AddVehicle());

        var quota = await _fixture.RoutePlanningQueries.Handle(new GetActiveRouteQuotaQuery(Org), Ct);

        quota.ShouldBe(new ActiveRouteQuota("BASIC", 1, 2));
        quota.RemainingActiveRoutes.ShouldBe(1);
        quota.LimitReached.ShouldBeFalse();
    }
}
