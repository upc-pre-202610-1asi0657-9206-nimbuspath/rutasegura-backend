using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Services;
using RutaSegura.Monolith.Administration.Tests.Support;
using static RutaSegura.Monolith.Administration.Tests.Support.AdministrationMother;

namespace RutaSegura.Monolith.Administration.Tests.Domain;

/// <summary>US-05 S2: a driver or vehicle cannot serve two routes with overlapping schedules.</summary>
public sealed class RouteScheduleConflictPolicyTests
{
    private readonly Driver _driver = NewDriver();
    private readonly Vehicle _vehicle = NewVehicle();

    [Fact]
    public void SameDriverOnOverlappingRoute_ThrowsResourceScheduleConflict()
    {
        var busy = ReadyRoute(_driver, NewVehicle(), name: "Ruta 1");
        var candidate = NewRoute("Ruta 2", Schedule(new TimeOnly(7, 30), new TimeOnly(8, 30)));

        Should.Throw<AdministrationDomainException>(() =>
                RouteScheduleConflictPolicy.EnsureNoConflict(candidate, candidate.Schedule, _driver.Id, _vehicle.Id,
                    [busy]))
            .Error.ShouldBe(AdministrationError.ResourceScheduleConflict);
    }

    [Fact]
    public void SameVehicleOnOverlappingRoute_ThrowsResourceScheduleConflict()
    {
        var busy = ReadyRoute(NewDriver(), _vehicle, name: "Ruta 1");
        var candidate = NewRoute("Ruta 2");

        Should.Throw<AdministrationDomainException>(() =>
                RouteScheduleConflictPolicy.EnsureNoConflict(candidate, candidate.Schedule, _driver.Id, _vehicle.Id,
                    [busy]))
            .Error.ShouldBe(AdministrationError.ResourceScheduleConflict);
    }

    [Fact]
    public void SameDriverOnBackToBackRoute_IsAllowed()
    {
        var busy = ReadyRoute(_driver, _vehicle, name: "Ruta 1"); // 06:45 - 07:50
        var candidate = NewRoute("Ruta 2", Schedule(new TimeOnly(7, 50), new TimeOnly(8, 40)));

        Should.NotThrow(() =>
            RouteScheduleConflictPolicy.EnsureNoConflict(candidate, candidate.Schedule, _driver.Id, _vehicle.Id,
                [busy]));
    }

    [Fact]
    public void SameDriverOnInactiveRoute_IsAllowed()
    {
        var inactive = ReadyRoute(_driver, _vehicle, name: "Ruta 1");
        inactive.Activate(_driver, _vehicle, 0, Plan(), Now);
        inactive.Deactivate();
        var candidate = NewRoute("Ruta 2");

        Should.NotThrow(() =>
            RouteScheduleConflictPolicy.EnsureNoConflict(candidate, candidate.Schedule, _driver.Id, _vehicle.Id,
                [inactive]));
    }

    [Fact]
    public void OtherResourcesOrTheSameRoute_AreIgnored()
    {
        var unrelated = ReadyRoute(NewDriver(), NewVehicle(), name: "Ruta 1");
        var route = ReadyRoute(_driver, _vehicle, name: "Ruta 2");

        Should.NotThrow(() => RouteScheduleConflictPolicy.EnsureNoConflict(route, [unrelated, route]));
    }
}
