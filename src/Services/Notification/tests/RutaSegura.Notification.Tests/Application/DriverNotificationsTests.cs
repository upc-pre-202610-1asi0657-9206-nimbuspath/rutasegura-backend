using RutaSegura.Notification.Application.Contracts.V1;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Tests.Fakes;

namespace RutaSegura.Notification.Tests.Application;

public sealed class DriverNotificationsTests : NotificationTestContext
{
    private static readonly Guid Driver = Guid.NewGuid();
    private void AuthorizeDriver()
    {
        Directory.TripDriver = Driver;
        Directory.Drivers.Add(new(Organization, Driver));
        User.Current = new(Driver, Organization);
    }

    [Theory]
    [InlineData("scheduled", "TripAssigned")]
    [InlineData("cancelled", "TripCancelled")]
    [InlineData("sos", "SosResolved")]
    [InlineData("absence", "StudentAbsent")]
    public async Task DriverEvent_IsDurableAuthorizedAndIdempotent(string kind, string category)
    {
        AuthorizeDriver();
        TripNotificationEventV1 payload = kind switch
        {
            "scheduled" => new DriverTripScheduledV1(Organization, Trip),
            "cancelled" => new DriverTripCancelledV1(Organization, Trip),
            "sos" => new DriverSosResolvedV1(Organization, Trip),
            _ => new DriverStudentAbsentV1(Organization, Trip, Student, "Guardian")
        };
        var command = Event(payload);
        (await Commands.Handle(command, Ct)).Value.Created.ShouldBe(1);
        (await Commands.Handle(command, Ct)).Value.Duplicates.ShouldBe(1);
        var notice = (await Queries.Handle(new GetMyNotificationsQuery(), Ct)).Value.ShouldHaveSingleItem();
        notice.Category.ShouldBe(category);
        notice.RecipientUserId.ShouldBe(Driver);
        notice.StudentId.ShouldBeNull(); // Family authorization is not substituted for driver authorization.
        Store.PendingDispatch.ShouldHaveSingleItem();
        Directory.Drivers.Clear(); // Suspension/revocation blocks historical reads and marking as read.
        (await Queries.Handle(new GetMyNotificationQuery(notice.Id), Ct)).IsFailure.ShouldBeTrue();
        (await Commands.Handle(new MarkNotificationAsReadCommand(notice.Id), Ct)).IsFailure.ShouldBeTrue();
        (await Queries.Handle(new GetMyNotificationsQuery(), Ct)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task OwnAbsence_DoesNotNotifyDriverAgain()
    {
        AuthorizeDriver();
        (await Commands.Handle(Event(new DriverStudentAbsentV1(Organization, Trip, Student, "Driver")), Ct))
            .Value.SkippedReason.ShouldBe("DRIVER_ALREADY_RECORDED_ABSENCE");
        Store.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task UnavailableTripContext_DoesNotCommitEmptyInbox()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => Commands.Handle(Event(new DriverTripCancelledV1(Organization, Trip)), Ct));
        Store.ProcessedEvents.ShouldBeEmpty();
        UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task ForeignOrUnassignedDriver_RejectsWholeTransaction()
    {
        AuthorizeDriver();
        Directory.ReturnUnfilteredRecipients = true;
        Directory.Drivers.Add(new(OtherOrganization, OtherGuardian));
        (await Commands.Handle(Event(new DriverTripScheduledV1(Organization, Trip)), Ct)).IsFailure.ShouldBeTrue();
        Store.Notifications.ShouldBeEmpty();
        Store.ProcessedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Announcement_IncludesDriverOnceAndRevalidatesAccess()
    {
        AuthorizeDriver();
        Directory.Drivers.Add(new(Organization, Driver));
        User.Current = new(Operator, Organization, true);
        Directory.AnnouncementRecipients.Clear();
        var command = new CreateAnnouncementCommand(Guid.NewGuid(), "Cambio operativo", "Revisa tu agenda.");
        (await Commands.Handle(command, Ct)).Value.NotificationsCreated.ShouldBe(1);
        User.Current = new(Driver, Organization);
        var notice = (await Queries.Handle(new GetMyNotificationsQuery(), Ct)).Value.ShouldHaveSingleItem();
        Directory.Drivers.Clear();
        (await Queries.Handle(new GetMyNotificationQuery(notice.Id), Ct)).IsFailure.ShouldBeTrue();
    }
}
