using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Tests.Fakes;

namespace RutaSegura.Notification.Tests.Application;

public sealed class PushSubscriptionTests : NotificationTestContext
{
    [Fact]
    public async Task ReactivatingADisabledInstallationCannotBypassTheEnabledLimit()
    {
        var first=await Commands.Handle(new RegisterPushSubscriptionCommand(Guid.NewGuid(),"disabled-token","Web"),Ct);
        await Commands.Handle(new DisablePushSubscriptionCommand(first.Value.Id),Ct);
        for(var i=0;i<20;i++) (await Commands.Handle(new RegisterPushSubscriptionCommand(Guid.NewGuid(),"token-"+i,"Web"),Ct)).IsSuccess.ShouldBeTrue();
        var disabled=Store.Subscriptions.Single(s=>s.Id==first.Value.Id);
        (await Commands.Handle(new RegisterPushSubscriptionCommand(disabled.InstallationId,"reactivated-token","Web"),Ct))
            .FirstError.Code.ShouldBe("PUSH_SUBSCRIPTION_LIMIT");
        disabled.IsEnabled.ShouldBeFalse();
    }
    [Fact]
    public async Task RegisterRefreshAndDisableAreOwnedAndIdempotent()
    {
        var input = new RegisterPushSubscriptionCommand(Guid.NewGuid(), "private-token", "Web");
        var first = await Commands.Handle(input, Ct);
        var repeated = await Commands.Handle(input with { Token = "rotated-token" }, Ct);
        repeated.Value.Id.ShouldBe(first.Value.Id);
        Store.Subscriptions.ShouldHaveSingleItem().Token.ShouldBe("rotated-token");
        await Commands.Handle(new DisablePushSubscriptionCommand(first.Value.Id), Ct);
        await Commands.Handle(new DisablePushSubscriptionCommand(first.Value.Id), Ct);
        Store.Subscriptions.Single().IsEnabled.ShouldBeFalse();
        input.ToString().ShouldNotContain(input.Token);
        System.Text.Json.JsonSerializer.Serialize(first.Value).ShouldNotContain(input.Token);
    }

    [Theory]
    [InlineData("", "Web")]
    [InlineData("with space", "Web")]
    [InlineData("token", "0")]
    [InlineData("token", "Desktop")]
    public async Task InvalidTokensAndPlatformsNeverCommit(string token, string platform)
    {
        (await Commands.Handle(new RegisterPushSubscriptionCommand(Guid.NewGuid(), token, platform), Ct)).IsFailure.ShouldBeTrue();
        UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task ForeignUserAndForeignOrganizationCannotRevokeASubscription()
    {
        var created = (await Commands.Handle(new RegisterPushSubscriptionCommand(Guid.NewGuid(), "secret", "Web"), Ct)).Value;
        User.Current = new(OtherGuardian, Organization);
        (await Commands.Handle(new DisablePushSubscriptionCommand(created.Id), Ct)).FirstError.Code.ShouldBe("PUSH_SUBSCRIPTION_NOT_FOUND");
        User.Current = new(Guardian, OtherOrganization);
        (await Commands.Handle(new DisablePushSubscriptionCommand(created.Id), Ct)).IsFailure.ShouldBeTrue();
        Store.Subscriptions.Single().IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task AnonymousWorkerCannotRegisterAnInstallation()
    {
        User.Current = null;
        (await Commands.Handle(new RegisterPushSubscriptionCommand(Guid.NewGuid(), "secret", "Web"), Ct)).FirstError.Code.ShouldBe("NOTIFICATION_AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async Task RevokedAnnouncementAudienceHidesTheNoticeAndDeniesMarkAsRead()
    {
        User.Current = new(Operator, Organization, true);
        await Commands.Handle(new CreateAnnouncementCommand(Guid.NewGuid(), "Aviso", "Mensaje"), Ct);
        var notice = Store.Notifications.Single();
        User.Current = new(Guardian, Organization);
        (await Queries.Handle(new RutaSegura.Notification.Application.Model.Queries.GetMyNotificationQuery(notice.Id), Ct)).IsSuccess.ShouldBeTrue();
        Directory.AnnouncementRecipients.Clear();
        (await Queries.Handle(new RutaSegura.Notification.Application.Model.Queries.GetMyNotificationQuery(notice.Id), Ct)).FirstError.Code.ShouldBe("NOTIFICATION_NOT_FOUND");
        (await Commands.Handle(new MarkNotificationAsReadCommand(notice.Id), Ct)).IsFailure.ShouldBeTrue();
    }
}
