using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Commands;
using RutaSegura.Monolith.Administration.Tests.Support;
using static RutaSegura.Monolith.Administration.Tests.Support.AdministrationMother;

namespace RutaSegura.Monolith.Administration.Tests.Application;

public sealed class RosterCommandServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly AdministrationFixture _fixture = new();

    private RegisterDriverCommand RegisterCarlos(string document = "45678912") => new(
        _fixture.OrganizationId, Guid.NewGuid(), "Carlos", "Quispe", document, "Q45678912", "A-IIb",
        Today.AddYears(3), "987654321");

    // ── Drivers (US-01) ──────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterDriver_ValidData_PersistsADriverPendingVerification()
    {
        var result = await _fixture.Roster.Handle(RegisterCarlos(), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        result.Value!.Status.IsPendingVerification().ShouldBeTrue();
        result.Value.OrganizationId.ShouldBe(Organization);
        _fixture.Drivers.Items.ShouldHaveSingleItem();
        _fixture.UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task RegisterDriver_DocumentAlreadyRegistered_FailsWithoutCreatingADuplicate()
    {
        await _fixture.Roster.Handle(RegisterCarlos("45678912"), Ct);

        var result = await _fixture.Roster.Handle(RegisterCarlos("45678912"), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AdministrationError.DriverAlreadyRegistered);
        _fixture.Drivers.Items.Count.ShouldBe(1);
        _fixture.UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task RegisterDriver_AccountIsNotADriverOfTheOrganization_FailsWithDriverAccountNotValid()
    {
        _fixture.Iam.IsDriverAccount = false;

        var result = await _fixture.Roster.Handle(RegisterCarlos(), Ct);

        result.Error.ShouldBe(AdministrationError.DriverAccountNotValid);
        _fixture.Drivers.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task RegisterDriver_InvalidLicenseCategory_FailsWithInvalidData()
    {
        var result = await _fixture.Roster.Handle(RegisterCarlos() with { LicenseCategory = "Z" }, Ct);

        result.Error.ShouldBe(AdministrationError.InvalidAdministrationData);
        _fixture.UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task RegisterDriver_WithoutOrganization_FailsWithInvalidData() =>
        (await _fixture.Roster.Handle(RegisterCarlos() with { OrganizationId = Guid.Empty }, Ct))
        .Error.ShouldBe(AdministrationError.InvalidAdministrationData);

    [Fact]
    public async Task VerifyDriver_PendingDriver_BecomesAssignable()
    {
        var driver = _fixture.AddDriver(verified: false);

        var result = await _fixture.Roster.Handle(new VerifyDriverCommand(_fixture.OrganizationId, driver.Id.Identifier),
            Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        driver.IsAssignableOn(Today).ShouldBeTrue();
        driver.VerifiedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task VerifyDriver_OfAnotherOrganization_FailsAsNotFound()
    {
        var foreign = _fixture.AddDriver(OtherOrganization, verified: false);

        var result = await _fixture.Roster.Handle(new VerifyDriverCommand(_fixture.OrganizationId, foreign.Id.Identifier),
            Ct);

        result.Error.ShouldBe(AdministrationError.DriverNotFound);
        foreign.Status.IsPendingVerification().ShouldBeTrue();
    }

    [Fact]
    public async Task SuspendDriver_OperatingAnActiveRoute_FailsWithDriverHasActiveRoutes()
    {
        var driver = _fixture.AddDriver();
        _fixture.AddActiveRoute(driver, _fixture.AddVehicle());

        var result = await _fixture.Roster.Handle(new SuspendDriverCommand(_fixture.OrganizationId, driver.Id.Identifier),
            Ct);

        result.Error.ShouldBe(AdministrationError.DriverHasActiveRoutes);
        driver.Status.IsVerified().ShouldBeTrue();
    }

    [Fact]
    public async Task SuspendDriver_OnlyOnDraftRoutes_IsSuspended()
    {
        var driver = _fixture.AddDriver();
        _fixture.AddReadyRoute(driver, _fixture.AddVehicle());

        var result = await _fixture.Roster.Handle(new SuspendDriverCommand(_fixture.OrganizationId, driver.Id.Identifier),
            Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        driver.Status.IsSuspended().ShouldBeTrue();
    }

    // ── Vehicles (US-08) ─────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterVehicle_SamePlateWrittenDifferently_FailsWithPlateAlreadyRegistered()
    {
        await _fixture.Roster.Handle(new RegisterVehicleCommand(_fixture.OrganizationId, "ABC-123", 15, "Toyota", "Hiace"), Ct);

        var result = await _fixture.Roster.Handle(
            new RegisterVehicleCommand(_fixture.OrganizationId, "abc123", 20, "Hyundai", "County"), Ct);

        result.Error.ShouldBe(AdministrationError.VehiclePlateAlreadyRegistered);
        _fixture.Vehicles.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RegisterVehicle_SamePlateInAnotherOrganization_IsAllowed()
    {
        await _fixture.Roster.Handle(new RegisterVehicleCommand(OtherOrganization.Identifier, "ABC-123", 15, "Toyota", "Hiace"), Ct);

        var result = await _fixture.Roster.Handle(
            new RegisterVehicleCommand(_fixture.OrganizationId, "ABC-123", 15, "Toyota", "Hiace"), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task DisableVehicle_ServingAnActiveRoute_FailsWithVehicleHasActiveRoutes()
    {
        var vehicle = _fixture.AddVehicle();
        _fixture.AddActiveRoute(_fixture.AddDriver(), vehicle);

        var result = await _fixture.Roster.Handle(new DisableVehicleCommand(_fixture.OrganizationId, vehicle.Id.Identifier),
            Ct);

        result.Error.ShouldBe(AdministrationError.VehicleHasActiveRoutes);
        vehicle.IsAssignable().ShouldBeTrue();
    }

    [Fact]
    public async Task ChangeVehicleCapacity_BelowStudentsPlannedOnItsRoutes_FailsWithCapacityExceeded()
    {
        var vehicle = _fixture.AddVehicle(capacity: 15);
        _fixture.AddReadyRoute(_fixture.AddDriver(), vehicle, students: 6);

        var result = await _fixture.Roster.Handle(
            new ChangeVehicleCapacityCommand(_fixture.OrganizationId, vehicle.Id.Identifier, 5), Ct);

        result.Error.ShouldBe(AdministrationError.VehicleCapacityExceeded);
        vehicle.Capacity.Value.ShouldBe(15);
    }
}
