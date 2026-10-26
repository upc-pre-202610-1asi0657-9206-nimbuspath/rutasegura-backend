using Monolith.Stakeholder.Application.Internal.OutboundServices.ACL;
using Monolith.Stakeholder.Domain.Model;
using Monolith.Stakeholder.Domain.Model.Commands;
using Monolith.Stakeholder.Domain.Model.Queries;
using RutaSegura.Monolith.Stakeholder.Tests.Support;
using static RutaSegura.Monolith.Stakeholder.Tests.Support.StakeholderMother;

namespace RutaSegura.Monolith.Stakeholder.Tests.Application;

public sealed class StakeholderCommandServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];
    private readonly StakeholderFixture _fixture = new();

    [Fact]
    public async Task Register_SavesTheChild_AndEnqueuesTheGuardianLink()
    {
        var result = await _fixture.Commands.Handle(new RegisterStudentCommand(Guardian, "Valeria", "Quispe",
            new DateOnly(2017, 5, 14), "3.° de primaria", null, "Av. Primavera 1240", null, -12.11, -76.98), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        _fixture.Students.Items.ShouldHaveSingleItem();
        var linked = _fixture.Outbox.Events.ShouldHaveSingleItem();
        linked.MessageType.ShouldBe("stakeholder.guardian-student-linked");
        _fixture.UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Register_BeyondTheLimitPerGuardian_IsRejected()
    {
        for (var i = 0; i < 10; i++) _fixture.AddStudent();

        var result = await _fixture.Commands.Handle(new RegisterStudentCommand(Guardian, "Once", "Quispe", null, null,
            null, "Av. Primavera 1240", null, -12.11, -76.98), Ct);

        result.Error.ShouldBe(StakeholderError.StudentLimitReached);
    }

    [Fact]
    public async Task Enroll_PlacesTheChildAtItsPickupPoint_InOneCommittedTransaction()
    {
        var student = _fixture.AddStudent();
        var route = _fixture.RoutePlanning.AddRoute("K7QM4P2X", Route());

        var result = await _fixture.Commands.Handle(new EnrollStudentCommand(Guardian, student.Id.Identifier, "k7qm-4p2x"), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        result.Value!.StopName.ShouldBe("Av. Primavera 1240, Surco");
        _fixture.RoutePlanning.Placed.ShouldHaveSingleItem()
            .ShouldBe((route.RouteId, student.Id.Identifier, -12.11234, -76.98567));
        student.IsEnrolledIn(route.RouteId).ShouldBeTrue();
        _fixture.UnitOfWork.CommittedTransactions.ShouldBe(1);
        _fixture.Outbox.Events.ShouldHaveSingleItem().MessageType.ShouldBe("stakeholder.student-enrolled");
    }

    [Fact]
    public async Task Enroll_WhenRoutePlanningRejects_RollsBackAndReportsItsError()
    {
        var student = _fixture.AddStudent();
        _fixture.RoutePlanning.AddRoute("K7QM4P2X", Route());
        _fixture.RoutePlanning.NextPlacementFailure = PlacementOutcome.Failed(StakeholderError.RouteFull, "Full");

        var result = await _fixture.Commands.Handle(new EnrollStudentCommand(Guardian, student.Id.Identifier, "K7QM4P2X"), Ct);

        result.Error.ShouldBe(StakeholderError.RouteFull);
        _fixture.UnitOfWork.RolledBackTransactions.ShouldBe(1);
        _fixture.UnitOfWork.CommittedTransactions.ShouldBe(0);
        _fixture.Outbox.Events.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("UNKNOWN1", true, true, StakeholderError.EnrollmentCodeNotFound)]
    [InlineData("K7QM4P2X", false, true, StakeholderError.RouteNotAcceptingEnrollments)]
    [InlineData("K7QM4P2X", true, false, StakeholderError.OrganizationNotActive)]
    public async Task Enroll_ChecksTheRouteBeforeTouchingAnything(string code, bool accepts, bool organizationActive,
        StakeholderError expected)
    {
        var student = _fixture.AddStudent();
        _fixture.RoutePlanning.AddRoute("K7QM4P2X", Route(accepts: accepts));
        _fixture.Directory.OrganizationActive = organizationActive;

        var result = await _fixture.Commands.Handle(new EnrollStudentCommand(Guardian, student.Id.Identifier, code), Ct);

        result.Error.ShouldBe(expected);
        _fixture.RoutePlanning.Placed.ShouldBeEmpty();
        _fixture.UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task ChildOfAnotherGuardian_IsNotFound()
    {
        var student = _fixture.AddStudent(Guid.NewGuid());
        _fixture.RoutePlanning.AddRoute("K7QM4P2X", Route());

        (await _fixture.Commands.Handle(new EnrollStudentCommand(Guardian, student.Id.Identifier, "K7QM4P2X"), Ct))
            .Error.ShouldBe(StakeholderError.StudentNotFound);
        (await _fixture.Commands.Handle(new RemoveStudentCommand(Guardian, student.Id.Identifier), Ct))
            .Error.ShouldBe(StakeholderError.StudentNotFound);
    }

    [Fact]
    public async Task ChangePickupLocation_ReplacesTheChildOnEveryActiveRoute()
    {
        var student = _fixture.AddStudent();
        var pickup = _fixture.RoutePlanning.AddRoute("AAAA2222", Route("PICKUP"));
        var back = _fixture.RoutePlanning.AddRoute("BBBB3333", Route("RETURN"));
        student.Enroll(pickup.RouteId, Organization, pickup.Name, "PICKUP", Now);
        student.Enroll(back.RouteId, Organization, back.Name, "RETURN", Now);

        var result = await _fixture.Commands.Handle(new ChangeStudentPickupLocationCommand(Guardian,
            student.Id.Identifier, "Calle Los Olivos 123", null, -12.1089, -76.9912), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        _fixture.RoutePlanning.Placed.Select(p => p.RouteId).ShouldBe([pickup.RouteId, back.RouteId], ignoreOrder: true);
        _fixture.RoutePlanning.Placed.ShouldAllBe(p => p.Latitude == -12.1089);
        _fixture.UnitOfWork.CommittedTransactions.ShouldBe(1);
    }

    [Fact]
    public async Task ChangePickupLocation_WhenARouteCannotTakeIt_RollsBackEverything()
    {
        var student = _fixture.AddStudent();
        var pickup = _fixture.RoutePlanning.AddRoute("AAAA2222", Route());
        student.Enroll(pickup.RouteId, Organization, pickup.Name, "PICKUP", Now);
        _fixture.RoutePlanning.NextPlacementFailure =
            PlacementOutcome.Failed(StakeholderError.RouteStopLimitReached, "30 stops");

        var result = await _fixture.Commands.Handle(new ChangeStudentPickupLocationCommand(Guardian,
            student.Id.Identifier, "Calle Los Olivos 123", null, -12.1089, -76.9912), Ct);

        result.Error.ShouldBe(StakeholderError.RouteStopLimitReached);
        result.Message.ShouldContain(pickup.Name);
        _fixture.UnitOfWork.RolledBackTransactions.ShouldBe(1);
    }

    [Fact]
    public async Task UploadPhoto_AcceptsImagesOnly()
    {
        var student = _fixture.AddStudent();

        (await _fixture.Commands.Handle(new UploadStudentPhotoCommand(Guardian, student.Id.Identifier, "GIF89a"u8.ToArray()), Ct))
            .Error.ShouldBe(StakeholderError.StudentPhotoInvalid);
        var ok = await _fixture.Commands.Handle(new UploadStudentPhotoCommand(Guardian, student.Id.Identifier, Png), Ct);

        ok.IsSuccess.ShouldBeTrue();
        student.Photo!.ContentType.ShouldBe("image/png");
        _fixture.Photos.Photos[student.Id.Identifier].Content.ShouldBe(Png);
    }

    [Fact]
    public async Task Remove_WithdrawsFromEveryRoute_DeletesThePhoto_AndUnlinks()
    {
        var student = _fixture.AddStudent();
        var route = _fixture.RoutePlanning.AddRoute("AAAA2222", Route());
        student.Enroll(route.RouteId, Organization, route.Name, "PICKUP", Now);
        student.ClearDomainEvents();
        await _fixture.Commands.Handle(new UploadStudentPhotoCommand(Guardian, student.Id.Identifier, Png), Ct);

        var result = await _fixture.Commands.Handle(new RemoveStudentCommand(Guardian, student.Id.Identifier), Ct);

        result.IsSuccess.ShouldBeTrue(result.Message);
        _fixture.RoutePlanning.Removed.ShouldHaveSingleItem().ShouldBe((route.RouteId, student.Id.Identifier));
        _fixture.Photos.Photos.ShouldBeEmpty();
        _fixture.Outbox.Events.Select(e => e.MessageType).ShouldBe(
            ["stakeholder.student-enrollment-ended", "stakeholder.guardian-student-unlinked"], ignoreOrder: false);
    }

    [Fact]
    public async Task Cancel_ByAnotherOrganization_IsNotFound()
    {
        var student = _fixture.AddStudent();
        var route = _fixture.RoutePlanning.AddRoute("AAAA2222", Route());
        var enrollment = student.Enroll(route.RouteId, Organization, route.Name, "PICKUP", Now);

        (await _fixture.Commands.Handle(new CancelEnrollmentCommand(Guid.NewGuid(), enrollment.Id.Identifier, null), Ct))
            .Error.ShouldBe(StakeholderError.EnrollmentNotFound);
        (await _fixture.Commands.Handle(new CancelEnrollmentCommand(Organization, enrollment.Id.Identifier, "Impago"), Ct))
            .IsSuccess.ShouldBeTrue();
        _fixture.RoutePlanning.Removed.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GuardianProfile_IsCreatedThenUpdated()
    {
        (await _fixture.Commands.Handle(new UpsertGuardianProfileCommand(Guardian, "987654321", null), Ct)).IsSuccess.ShouldBeTrue();
        (await _fixture.Commands.Handle(new UpsertGuardianProfileCommand(Guardian, "912345678", "45781236"), Ct)).IsSuccess.ShouldBeTrue();
        (await _fixture.Commands.Handle(new UpsertGuardianProfileCommand(Guardian, "12", null), Ct))
            .Error.ShouldBe(StakeholderError.InvalidStakeholderData);

        var profile = _fixture.Profiles.Items.ShouldHaveSingleItem();
        profile.PhoneNumber.Value.ShouldBe("912345678");
        profile.DocumentNumber.ShouldBe("45781236");
    }

    [Fact]
    public async Task Photo_IsVisibleToTheGuardianAdminAndDriverOfItsRoute_Only()
    {
        var student = _fixture.AddStudent();
        var route = _fixture.RoutePlanning.AddRoute("AAAA2222", Route());
        student.Enroll(route.RouteId, Organization, route.Name, "PICKUP", Now);
        await _fixture.Commands.Handle(new UploadStudentPhotoCommand(Guardian, student.Id.Identifier, Png), Ct);
        var driver = Guid.NewGuid();
        _fixture.RoutePlanning.OperatedRoutes[driver] = [route.RouteId];
        var id = student.Id.Identifier;

        (await _fixture.Queries.Handle(new GetStudentPhotoQuery(id, Guardian, "PARENT", null), Ct)).IsSuccess.ShouldBeTrue();
        (await _fixture.Queries.Handle(new GetStudentPhotoQuery(id, Guid.NewGuid(), "ADMIN", Organization), Ct)).IsSuccess.ShouldBeTrue();
        (await _fixture.Queries.Handle(new GetStudentPhotoQuery(id, driver, "DRIVER", Organization), Ct)).IsSuccess.ShouldBeTrue();

        (await _fixture.Queries.Handle(new GetStudentPhotoQuery(id, Guid.NewGuid(), "PARENT", null), Ct))
            .Error.ShouldBe(StakeholderError.StudentAccessDenied);
        (await _fixture.Queries.Handle(new GetStudentPhotoQuery(id, Guid.NewGuid(), "DRIVER", Organization), Ct))
            .Error.ShouldBe(StakeholderError.StudentAccessDenied);
        (await _fixture.Queries.Handle(new GetStudentPhotoQuery(id, Guid.NewGuid(), "ADMIN", Guid.NewGuid()), Ct))
            .Error.ShouldBe(StakeholderError.StudentAccessDenied);
    }
}
