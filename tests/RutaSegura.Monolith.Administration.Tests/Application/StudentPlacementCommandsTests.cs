using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Commands;
using Monolith.Administration.Domain.Model.ValueObjects;
using RutaSegura.Monolith.Administration.Tests.Support;

namespace RutaSegura.Monolith.Administration.Tests.Application;

/// <summary>Use cases behind the Route Planning facade the Stakeholder context calls.</summary>
public sealed class StudentPlacementCommandsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly AdministrationFixture _fixture = new();

    [Fact]
    public async Task PlaceStudent_OnAnActiveRoute_PublishesRouteUpdatedWithTheChild()
    {
        var driver = _fixture.AddDriver();
        var vehicle = _fixture.AddVehicle();
        var route = _fixture.AddReadyRoute(driver, vehicle);
        route.Activate(driver, vehicle, 0, AdministrationMother.Plan(), AdministrationMother.Now);
        route.ClearDomainEvents();
        var valeria = Guid.NewGuid();

        var result = await _fixture.RoutePlanning.Handle(new PlaceStudentOnRouteCommand(_fixture.OrganizationId,
            route.Id.Identifier, valeria, -12.13, -76.99, "Av. Benavides 4500"), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        route.StopOf(new StudentId(valeria)).ShouldNotBeNull().Origin.IsEnrollment().ShouldBeTrue();
        var updated = _fixture.Outbox.Events.ShouldHaveSingleItem();
        updated.MessageType.ShouldBe("administration.route-updated");
        _fixture.UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task PlaceStudent_UsesTheCapacityOfTheAssignedVehicle()
    {
        var driver = _fixture.AddDriver();
        var vehicle = _fixture.AddVehicle(capacity: 2);
        var route = _fixture.AddReadyRoute(driver, vehicle, students: 2);

        var result = await _fixture.RoutePlanning.Handle(new PlaceStudentOnRouteCommand(_fixture.OrganizationId,
            route.Id.Identifier, Guid.NewGuid(), -12.13, -76.99, "Av. Benavides 4500"), Ct);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(AdministrationError.VehicleCapacityExceeded);
        _fixture.UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task PlaceStudent_OnARouteOfAnotherOrganization_IsNotFound()
    {
        var driver = _fixture.AddDriver();
        var route = _fixture.AddReadyRoute(driver, _fixture.AddVehicle());

        var result = await _fixture.RoutePlanning.Handle(new PlaceStudentOnRouteCommand(Guid.NewGuid(),
            route.Id.Identifier, Guid.NewGuid(), -12.13, -76.99, null), Ct);

        result.Error.ShouldBe(AdministrationError.RouteNotFound);
    }

    [Fact]
    public async Task RemoveStudent_NotPlanned_SucceedsWithoutCommitting()
    {
        var route = _fixture.AddReadyRoute(_fixture.AddDriver(), _fixture.AddVehicle());

        var result = await _fixture.RoutePlanning.Handle(new RemoveStudentFromRouteCommand(_fixture.OrganizationId,
            route.Id.Identifier, Guid.NewGuid()), Ct);

        result.IsSuccess.ShouldBeTrue();
        _fixture.UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task RegenerateEnrollmentCode_ChangesTheCode_AndTheQueryFindsTheRouteByIt()
    {
        var route = _fixture.AddReadyRoute(_fixture.AddDriver(), _fixture.AddVehicle());
        var previous = route.EnrollmentCode;

        var result = await _fixture.RoutePlanning.Handle(
            new RegenerateRouteEnrollmentCodeCommand(_fixture.OrganizationId, route.Id.Identifier), Ct);

        result.IsSuccess.ShouldBeTrue();
        route.EnrollmentCode.ShouldNotBe(previous);
        (await _fixture.RoutePlanningQueries.Handle(
                new global::Monolith.Administration.Domain.Model.Queries.GetRouteByEnrollmentCodeQuery(
                    route.EnrollmentCode.Value.ToLowerInvariant()), Ct))
            .ShouldBe(route);
        (await _fixture.RoutePlanningQueries.Handle(
                new global::Monolith.Administration.Domain.Model.Queries.GetRouteByEnrollmentCodeQuery(previous.Value),
                Ct))
            .ShouldBeNull();
    }
}
