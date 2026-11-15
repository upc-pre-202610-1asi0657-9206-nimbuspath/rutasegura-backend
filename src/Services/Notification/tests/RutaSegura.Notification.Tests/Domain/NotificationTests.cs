using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Tests.Fakes;

namespace RutaSegura.Notification.Tests.Domain;

public sealed class NotificationTests : NotificationTestContext
{
    [Fact]
    public void Sos_HasCriticalPriority_WithoutRequiringLocation()
    {
        var notice = Notice(category: NotificationCategory.Sos);
        notice.Priority.ShouldBe(NotificationPriority.Critical);
        notice.ReadAt.ShouldBeNull();
    }

    [Fact]
    public void Read_IsIdempotent_AndKeepsFirstTimestamp()
    {
        var notice = Notice();
        notice.MarkAsRead(Now.AddMinutes(1)).ShouldBeTrue();
        notice.MarkAsRead(Now.AddMinutes(2)).ShouldBeFalse();
        notice.ReadAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void ReadBeforeCreation_IsRejected()
    {
        var error = Should.Throw<DomainException>(() => Notice().MarkAsRead(Now.AddSeconds(-1)));
        error.Code.ShouldBe("NOTIFICATION_INVALID_READ_TIME");
    }

    [Theory]
    [InlineData("", "contenido")]
    [InlineData("titulo", " ")]
    public void EmptyContent_IsRejected(string title, string body) =>
        Should.Throw<DomainException>(() => NotificationContent.Create(title, body)).Code.ShouldBe("NOTIFICATION_INVALID_CONTENT");

    [Fact]
    public void Content_IsTrimmed_AndLengthBounded()
    {
        NotificationContent.Create(" Aviso ", " Contenido ").Title.ShouldBe("Aviso");
        Should.Throw<DomainException>(() => NotificationContent.Create(new string('a', 121), "body"));
        Should.Throw<DomainException>(() => NotificationContent.Create("title", new string('a', 2001)));
    }

    [Fact]
    public void LogicalKey_UsesNonNullableSubject_ForMysqlUniqueIndex()
    {
        Notice().Key.SubjectId.ShouldBe(Guid.Empty);
        Notice(student: Student).Key.SubjectId.ShouldBe(Student);
    }

    [Fact]
    public void DeliveryAcceptance_IsIndependentOfReading()
    {
        var notice = Notice();
        var delivery = NotificationDelivery.Schedule(notice, NotificationChannel.Push, Guid.NewGuid(), Now);
        delivery.AcceptByProvider("fcm-message-reference", Now);
        delivery.Status.ShouldBe(DeliveryStatus.ProviderAccepted);
        delivery.AttemptCount.ShouldBe(1);
        notice.ReadAt.ShouldBeNull();
        Should.Throw<DomainException>(() => delivery.Fail("TIMEOUT", Now.AddSeconds(1))).Code.ShouldBe("DELIVERY_ALREADY_FINISHED");
    }

    [Fact]
    public void Retry_RequiresDueTime_AndPreservesAttempts()
    {
        var delivery = NotificationDelivery.Schedule(Notice(), NotificationChannel.Push, Guid.NewGuid(), Now);
        delivery.Retry("PROVIDER_UNAVAILABLE", Now.AddMinutes(1), Now);
        Should.Throw<DomainException>(() => delivery.AcceptByProvider("receipt", Now.AddSeconds(30)));
        delivery.AttemptCount.ShouldBe(1);
        delivery.AcceptByProvider("receipt", Now.AddMinutes(1));
        delivery.AttemptCount.ShouldBe(2);
        delivery.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public void SkippedPush_DoesNotDeleteInboxOrCountAsSendAttempt()
    {
        var notice = Notice();
        var delivery = NotificationDelivery.Schedule(notice, NotificationChannel.Push, Guid.NewGuid(), Now);
        delivery.Skip("PUSH_DISABLED");
        delivery.Status.ShouldBe(DeliveryStatus.Skipped);
        delivery.AttemptCount.ShouldBe(0);
        Store.Notifications.ShouldContain(notice);
    }

    [Fact]
    public void RetryInThePast_IsRejectedWithoutMutatingState()
    {
        var delivery = NotificationDelivery.Schedule(Notice(), NotificationChannel.Realtime, Guardian, Now);
        Should.Throw<DomainException>(() => delivery.Retry("TIMEOUT", Now, Now));
        delivery.Status.ShouldBe(DeliveryStatus.Pending);
        delivery.AttemptCount.ShouldBe(0);
    }
}
