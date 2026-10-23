using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Tests.Fakes;

namespace RutaSegura.Notification.Tests.Application;

public sealed class AnnouncementTests : NotificationTestContext
{
    private void Authorize() => User.Current = new NotificationUser(Operator, Organization, true);

    [Fact]
    public async Task Administrator_CreatesAnnouncementAndOneNoticePerUser()
    {
        Authorize();
        Directory.AnnouncementRecipients.Add(new(Organization, Guardian));
        var result = await Commands.Handle(new CreateAnnouncementCommand(Guid.NewGuid(), "Cambio de horario", "Salida a las 13:00."), Ct);
        result.Value.Created.ShouldBeTrue();
        result.Value.NotificationsCreated.ShouldBe(1);
        Store.Announcements.ShouldHaveSingleItem();
        Store.Notifications.ShouldHaveSingleItem().AnnouncementId.ShouldBe(result.Value.AnnouncementId);
        Store.PendingDispatch.ShouldHaveSingleItem();
        UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task RepeatedRequest_ReturnsSameAnnouncementWithoutDuplicatingDispatch()
    {
        Authorize();
        var command = new CreateAnnouncementCommand(Guid.NewGuid(), "Aviso", "Cambio de horario.");
        var first = await Commands.Handle(command, Ct);
        var second = await Commands.Handle(command, Ct);
        second.Value.AnnouncementId.ShouldBe(first.Value.AnnouncementId);
        second.Value.Created.ShouldBeFalse();
        Store.PendingDispatch.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ReusedRequestWithDifferentContent_IsConflict()
    {
        Authorize();
        var command = new CreateAnnouncementCommand(Guid.NewGuid(), "Aviso", "Contenido original");
        await Commands.Handle(command, Ct);
        var result = await Commands.Handle(command with { Body = "Contenido diferente" }, Ct);
        result.FirstError.Type.ShouldBe(ErrorType.Conflict);
        Store.Announcements.ShouldHaveSingleItem();
        UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Guardian_CannotPublishEvenWithValidContent()
    {
        var result = await Commands.Handle(new CreateAnnouncementCommand(Guid.NewGuid(), "Aviso", "Mensaje"), Ct);
        result.FirstError.Code.ShouldBe("ANNOUNCEMENT_ACCESS_DENIED");
        Store.Notifications.ShouldBeEmpty();
        UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task ForeignRoute_IsRejectedByDirectoryWithoutSavingAnything()
    {
        Authorize();
        Directory.AnnouncementError = Error.Forbidden("ANNOUNCEMENT_ROUTE_ACCESS_DENIED", "Ruta ajena");
        var result = await Commands.Handle(new CreateAnnouncementCommand(Guid.NewGuid(), "Aviso", "Mensaje", Guid.NewGuid()), Ct);
        result.FirstError.Code.ShouldBe("ANNOUNCEMENT_ROUTE_ACCESS_DENIED");
        Store.Announcements.ShouldBeEmpty();
        Store.PendingDispatch.ShouldBeEmpty();
        UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task DirectoryReturnsForeignRecipient_RejectsEntireAnnouncement()
    {
        Authorize();
        Directory.AnnouncementRecipients.Add(new(OtherOrganization, OtherGuardian));
        var result = await Commands.Handle(new CreateAnnouncementCommand(Guid.NewGuid(), "Aviso", "Mensaje"), Ct);
        result.FirstError.Code.ShouldBe("NOTIFICATION_INVALID_AUDIENCE");
        Store.Announcements.ShouldBeEmpty();
        Store.PendingDispatch.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "Mensaje")]
    [InlineData("Aviso", " ")]
    public async Task InvalidContent_DoesNotCommit(string title, string body)
    {
        Authorize();
        var result = await Commands.Handle(new CreateAnnouncementCommand(Guid.NewGuid(), title, body), Ct);
        result.FirstError.Code.ShouldBe("NOTIFICATION_INVALID_CONTENT");
        UnitOfWork.Commits.ShouldBe(0);
    }
}
