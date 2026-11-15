using Microsoft.Extensions.Time.Testing;
using Monolith.Administration.Application.Internal.CommandServices;
using Monolith.Administration.Application.Internal.QueryServices;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;
using static RutaSegura.Monolith.Administration.Tests.Support.AdministrationMother;

namespace RutaSegura.Monolith.Administration.Tests.Support;

/// <summary>Application services wired with in-memory ports and a fixed clock (Friday 08:00 in Lima).</summary>
internal sealed class AdministrationFixture
{
    public InMemoryDriverRepository Drivers { get; } = new();
    public InMemoryVehicleRepository Vehicles { get; } = new();
    public InMemoryRouteRepository Routes { get; } = new();
    public InMemoryOperationalAlertRepository Alerts { get; } = new();
    public InMemoryTripRecordRepository TripRecords { get; } = new();
    public CountingUnitOfWork UnitOfWork { get; } = new();
    public RecordingOutbox Outbox { get; } = new();
    public StubPlanLimitsProvider PlanLimits { get; } = new();
    public StubIamAccountService Iam { get; } = new();
    public FakeTimeProvider Clock { get; } = new(Now);

    public Guid OrganizationId => Organization.Identifier;

    public RosterCommandService Roster => new(Drivers, Vehicles, Routes, Iam, UnitOfWork, Clock);

    public RoutePlanningCommandService RoutePlanning =>
        new(Routes, Drivers, Vehicles, PlanLimits, Outbox, UnitOfWork, Clock);

    public OperationsMonitoringCommandService Monitoring => new(Alerts, TripRecords, Routes, UnitOfWork, Clock);

    public RoutePlanningQueryService RoutePlanningQueries => new(Routes, PlanLimits);

    public Driver AddDriver(OrganizationId? organization = null, bool verified = true)
    {
        var driver = NewDriver(organization, verified);
        Drivers.Items.Add(driver);
        return driver;
    }

    public Vehicle AddVehicle(int capacity = 15, OrganizationId? organization = null)
    {
        var vehicle = NewVehicle(organization, capacity);
        Vehicles.Items.Add(vehicle);
        return vehicle;
    }

    public Route AddRoute(Route route)
    {
        route.ClearDomainEvents();
        Routes.Items.Add(route);
        return route;
    }

    /// <summary>A DRAFT route with two stops, students and resources: ready to be activated.</summary>
    public Route AddReadyRoute(Driver driver, Vehicle vehicle, int students = 2, RouteSchedule? schedule = null,
        string name = "Ruta 3 - Colegio San Agustin") =>
        AddRoute(ReadyRoute(driver, vehicle, students, schedule, name));

    public Route AddActiveRoute(Driver driver, Vehicle vehicle, RouteSchedule? schedule = null,
        string name = "Ruta 3 - Colegio San Agustin")
    {
        var route = ReadyRoute(driver, vehicle, schedule: schedule, name: name);
        route.Activate(driver, vehicle, 0, Plan(), Now);
        return AddRoute(route);
    }
}
