using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Tests.Fakes;

namespace RutaSegura.Notification.Tests.Application;

public sealed class InboxTests : NotificationTestContext
{
    [Fact]
    public async Task Inbox_OnlyShowsOwnOrganizationAndCurrentStudentLinks()
    {
        var allowed = Notice(student: Student);
        Notice(student: OtherStudent);
        Notice(recipient: OtherGuardian, student: Student);
        Notice(organization: OtherOrganization, student: Student);
        var result = await Queries.Handle(new GetMyNotificationsQuery(), Ct);
        result.Value.ShouldHaveSingleItem().Id.ShouldBe(allowed.Id);
        UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task RevokedLink_HidesExistingNoticeAndPreventsMarkAsRead()
    {
        var notice = Notice(student: Student);
        (await Queries.Handle(new GetMyNotificationQuery(notice.Id), Ct)).IsSuccess.ShouldBeTrue();
        Directory.Students.Clear();
        (await Queries.Handle(new GetMyNotificationQuery(notice.Id), Ct)).FirstError.Code.ShouldBe("NOTIFICATION_NOT_FOUND");
        (await Commands.Handle(new MarkNotificationAsReadCommand(notice.Id), Ct)).FirstError.Code.ShouldBe("NOTIFICATION_NOT_FOUND");
        notice.ReadAt.ShouldBeNull();
        UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task MarkAsRead_IsIdempotentAndDoesNotEnqueueAnotherDelivery()
    {
        var notice = Notice(student: Student);
        var command = new MarkNotificationAsReadCommand(notice.Id);
        await Commands.Handle(command, Ct);
        Clock.Now = Now.AddMinutes(1);
        var result = await Commands.Handle(command, Ct);
        result.Value.ReadAt.ShouldBe(Now);
        Store.PendingDispatch.ShouldBeEmpty();
    }

    [Fact]
    public async Task ForeignRecipient_CannotReadOrMarkAnotherUsersNotice()
    {
        var notice = Notice(recipient: OtherGuardian, student: Student);
        (await Queries.Handle(new GetMyNotificationQuery(notice.Id), Ct)).FirstError.Type.ShouldBe(ErrorType.NotFound);
        (await Commands.Handle(new MarkNotificationAsReadCommand(notice.Id), Ct)).IsFailure.ShouldBeTrue();
        notice.ReadAt.ShouldBeNull();
    }

    [Fact]
    public async Task ForeignOrganization_CannotReadNoticeEvenWithSameUserId()
    {
        var notice = Notice(organization: OtherOrganization);
        (await Queries.Handle(new GetMyNotificationQuery(notice.Id), Ct)).FirstError.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task UnauthenticatedAccess_IsRejectedWithoutPersisting()
    {
        User.Current = null;
        (await Queries.Handle(new GetMyNotificationsQuery(), Ct)).FirstError.Type.ShouldBe(ErrorType.Unauthorized);
        (await Commands.Handle(new MarkNotificationAsReadCommand(Guid.NewGuid()), Ct)).FirstError.Type.ShouldBe(ErrorType.Unauthorized);
        UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task PaginationFiltersAuthorizationBeforeTakingPage()
    {
        for (var i = 0; i < 5; i++) Notice(student: OtherStudent);
        var allowed = Notice(student: Student);
        var page = await Queries.Handle(new GetMyNotificationsQuery(Limit: 1), Ct);
        page.Value.ShouldHaveSingleItem().Id.ShouldBe(allowed.Id);
    }

    [Fact]
    public async Task UnreadFilter_ExcludesReadNoticesButKeepsOwnGeneralAnnouncements()
    {
        var read = Notice(student: Student);
        read.MarkAsRead(Now);
        var general = Notice();
        var result = await Queries.Handle(new GetMyNotificationsQuery(UnreadOnly: true), Ct);
        result.Value.ShouldHaveSingleItem().Id.ShouldBe(general.Id);
    }

    [Theory]
    [InlineData(-1, 25)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task InvalidPagination_IsRejected(int offset, int limit) =>
        (await Queries.Handle(new GetMyNotificationsQuery(offset, limit), Ct)).FirstError.Code.ShouldBe("NOTIFICATION_INVALID_PAGE");

    [Fact]
    public async Task DirectoryUnavailable_DoesNotFallBackToStaleJwtLinks()
    {
        var notice = Notice(student: Student);
        Directory.Unavailable = true;
        await Should.ThrowAsync<TimeoutException>(() => Queries.Handle(new GetMyNotificationQuery(notice.Id), Ct));
        UnitOfWork.Commits.ShouldBe(0);
    }
}
