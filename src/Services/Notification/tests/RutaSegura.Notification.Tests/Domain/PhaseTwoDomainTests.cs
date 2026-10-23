using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Tests.Fakes;

namespace RutaSegura.Notification.Tests.Domain;

public sealed class PhaseTwoDomainTests : NotificationTestContext
{
    [Fact]
    public void NoticeWithoutStudentUsesNonNullableDeduplicationSubject()
    {
        var notice = Notice();
        notice.SubjectId.ShouldBe(Guid.Empty);
        notice.Key.SubjectId.ShouldBe(Guid.Empty);
        var studentNotice = Notice(student: Student);
        studentNotice.SubjectId.ShouldBe(Student);
    }

    [Theory]
    [InlineData("")]
    [InlineData("token with spaces")]
    public void InvalidPushTokenCannotBeRegistered(string token) =>
        Should.Throw<DomainException>(() => PushSubscription.Register(Organization, Guardian, Guid.NewGuid(), token, PushPlatform.Web, Now));

    [Fact]
    public void PushSubscriptionRefreshKeepsOwnerAndInstallation_AndCanBeDisabled()
    {
        var installation = Guid.NewGuid();
        var subscription = PushSubscription.Register(Organization, Guardian, installation, "old-token", PushPlatform.Web, Now);
        subscription.Disable(Now.AddMinutes(1));
        subscription.IsEnabled.ShouldBeFalse();
        subscription.Refresh("new-token", PushPlatform.Android, Now.AddMinutes(2));
        subscription.IsEnabled.ShouldBeTrue();
        subscription.Token.ShouldBe("new-token");
        subscription.UserId.ShouldBe(Guardian);
        subscription.OrganizationId.ShouldBe(Organization);
        subscription.InstallationId.ShouldBe(installation);
        Should.Throw<DomainException>(() => subscription.Disable(Now));
    }

    [Fact]
    public void DispatchExpansionIsIdempotent_AndDoesNotMarkNoticeAsRead()
    {
        var notice = Notice();
        var job = NotificationDispatchJob.Schedule(notice);
        job.MarkExpanded(Now.AddMinutes(1)).ShouldBeTrue();
        job.MarkExpanded(Now.AddMinutes(2)).ShouldBeFalse();
        job.ExpandedAt.ShouldBe(Now.AddMinutes(1));
        notice.ReadAt.ShouldBeNull();
    }

    [Fact]
    public void InvalidProcessedEventCannotBeMarkedSuccessful() =>
        Should.Throw<DomainException>(() => ProcessedNotificationEvent.Record("notification.trip-events.v1", Guid.NewGuid(),
            Organization, "trip.student-boarded.v1", "not-a-hash", Now, Now, 0, null));
}
