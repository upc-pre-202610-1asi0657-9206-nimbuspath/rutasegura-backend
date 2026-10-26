using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Tests.Fakes;
using NoticeEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Tests.Application;

public sealed class NotificationPagingTests : NotificationTestContext
{
    private NoticeEntity AddAt(DateTimeOffset created, bool revoked = false)
    {
        var item = NoticeEntity.Create(Organization, Guardian, Guid.NewGuid(), "paging-test", "paging-test",
            revoked ? NotificationCategory.Sos : NotificationCategory.Boarding, NotificationContent.Create("Aviso", "Contenido"),
            created, created, studentId: revoked ? null : Student);
        Store.Add(item);
        return item;
    }

    [Fact]
    public async Task LegacyPageSkipsRevokedNonStudentNoticesBeforeApplyingAuthorizedOffset()
    {
        for (var i=0; i<110; i++) AddAt(Now.AddSeconds(-i), revoked:true);
        var first = AddAt(Now.AddMinutes(-10));
        var second = AddAt(Now.AddMinutes(-11));
        (await Queries.Handle(new GetMyNotificationsQuery(Limit:1), Ct)).Value.ShouldHaveSingleItem().Id.ShouldBe(first.Id);
        (await Queries.Handle(new GetMyNotificationsQuery(Offset:1, Limit:1), Ct)).Value.ShouldHaveSingleItem().Id.ShouldBe(second.Id);
    }

    [Fact]
    public async Task SignedCursorHandlesTimestampTiesUnreadChangesAndConcurrentNewItemsWithoutDuplicates()
    {
        var expected = Enumerable.Range(0,7).Select(_ => AddAt(Now)).Select(n=>n.Id).ToHashSet();
        var first = (await Queries.Handle(new GetMyNotificationPageQuery(Limit:2,UnreadOnly:true), Ct)).Value;
        var ids = first.Items.Select(n=>n.Id).ToList();
        Store.Notifications.Single(n=>n.Id==ids[0]).MarkAsRead(Now);
        Clock.Now=Now.AddSeconds(1);
        AddAt(Clock.Now);
        var cursor=first.NextCursor;
        for(var iteration=0; cursor is not null && iteration<10; iteration++)
        {
            var page=(await Queries.Handle(new GetMyNotificationPageQuery(cursor,2,true),Ct)).Value;
            ids.AddRange(page.Items.Select(n=>n.Id)); cursor=page.NextCursor;
        }
        cursor.ShouldBeNull();
        ids.Count.ShouldBe(7);
        ids.ToHashSet().SetEquals(expected).ShouldBeTrue();
    }

    [Theory]
    [InlineData("user")]
    [InlineData("organization")]
    [InlineData("filter")]
    [InlineData("expired")]
    [InlineData("tampered")]
    public async Task CursorRejectsChangedIdentityFilterExpirationOrPayload(string scenario)
    {
        AddAt(Now); AddAt(Now);
        var cursor=(await Queries.Handle(new GetMyNotificationPageQuery(Limit:1),Ct)).Value.NextCursor!;
        if(scenario=="user") User.Current=new(OtherGuardian,Organization);
        if(scenario=="organization") User.Current=new(Guardian,OtherOrganization);
        if(scenario=="expired") Clock.Now=Now.AddMinutes(31);
        if(scenario=="tampered") cursor=(cursor[0]=='A'?'B':'A')+cursor[1..];
        (await Queries.Handle(new GetMyNotificationPageQuery(cursor,1,scenario=="filter"),Ct))
            .FirstError.Code.ShouldBe("NOTIFICATION_INVALID_CURSOR");
    }

    [Fact]
    public async Task CursorAdvancesAcrossFullyRevokedBlocks_InsteadOfPretendingTheHistoryEnded()
    {
        for(var i=0;i<2000;i++) AddAt(Now.AddSeconds(-i),revoked:true);
        var allowed=AddAt(Now.AddDays(-1));
        var first=(await Queries.Handle(new GetMyNotificationPageQuery(Limit:1),Ct)).Value;
        first.Items.ShouldBeEmpty(); first.HasMore.ShouldBeTrue();
        (await Queries.Handle(new GetMyNotificationPageQuery(first.NextCursor,1),Ct)).Value.Items.ShouldHaveSingleItem().Id.ShouldBe(allowed.Id);
        await Should.ThrowAsync<TimeoutException>(()=>Queries.Handle(new GetMyNotificationsQuery(Limit:1),Ct));
    }
}
