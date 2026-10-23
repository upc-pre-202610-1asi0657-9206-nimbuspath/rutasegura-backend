using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Contracts.V1;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Tests.Fakes;

namespace RutaSegura.Notification.Tests.Application;

public sealed class ReceiveTripEventTests : NotificationTestContext
{
    [Theory]
    [InlineData("boarded", NotificationCategory.Boarding)]
    [InlineData("alighted", NotificationCategory.Alighting)]
    [InlineData("proximity", NotificationCategory.Proximity)]
    [InlineData("incident", NotificationCategory.Incident)]
    [InlineData("sos", NotificationCategory.Sos)]
    [InlineData("completed", NotificationCategory.Arrival)]
    public async Task ConfirmedEvent_CreatesAuthorizedNoticesAndDurableDispatch(string kind, NotificationCategory category)
    {
        TripNotificationEventV1 payload = kind switch
        {
            "alighted" => new StudentAlightedV1(Organization, Trip, Student, Stop),
            "proximity" => new VehicleApproachingStopV1(Organization, Trip, Stop, [Student], 3),
            "incident" => new IncidentReportedV1(Organization, Trip, Guid.NewGuid(), true, [Student]),
            "sos" => new SosRaisedV1(Organization, Trip, Guid.NewGuid(), Guid.NewGuid(), [Student]),
            "completed" => new TripCompletedV1(Organization, Trip, Guid.NewGuid(), TripDirectionV1.Pickup, [Student]),
            _ => new StudentBoardedV1(Organization, Trip, Student, Stop)
        };
        var result = await Commands.Handle(Event(payload), Ct);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Created.ShouldBe(kind == "sos" ? 2 : 1);
        Store.Notifications.ShouldAllBe(n => n.Category == category && n.OrganizationId == Organization &&
            n.CorrelationId == "test-correlation" && n.ReadAt == null);
        Store.PendingDispatch.Count.ShouldBe(Store.Notifications.Count);
        UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task SameMessageId_Retried_DoesNotCreateAnotherNoticeOrDispatch()
    {
        var command = Event();
        var first = await Commands.Handle(command, Ct);
        var second = await Commands.Handle(command, Ct);
        first.Value.Created.ShouldBe(1);
        second.Value.Created.ShouldBe(0);
        second.Value.Duplicates.ShouldBe(1);
        Store.Notifications.ShouldHaveSingleItem();
        Store.PendingDispatch.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task DuplicateDirectoryEntries_DoNotDuplicateRecipient()
    {
        Directory.Students.Add(new(Organization, Guardian, Student));
        (await Commands.Handle(Event(), Ct)).Value.Created.ShouldBe(1);
    }

    [Fact]
    public async Task SameGuardianWithTwoStudents_GetsOneNoticePerConfirmedStudent()
    {
        Directory.Students.Add(new(Organization, Guardian, OtherStudent));
        var result = await Commands.Handle(Event(new TripCompletedV1(Organization, Trip,
            Guid.NewGuid(), TripDirectionV1.Pickup, [Student, OtherStudent, Student])), Ct);
        result.Value.Created.ShouldBe(2);
        Store.Notifications.Select(n => n.Key).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task IncidentWithoutGuardianNotice_DoesNotSend()
    {
        var result = await Commands.Handle(Event(new IncidentReportedV1(Organization, Trip, Guid.NewGuid(), false, [Student])), Ct);
        result.Value.SkippedReason.ShouldBe("GUARDIAN_NOTICE_NOT_REQUESTED");
        Store.Notifications.ShouldBeEmpty();
        Store.PendingDispatch.ShouldBeEmpty();
    }

    [Fact]
    public async Task SosWithoutStudentsOrGps_StillNotifiesOperator()
    {
        var result = await Commands.Handle(Event(new SosRaisedV1(Organization, Trip, Guid.NewGuid(), Guid.NewGuid(), [])), Ct);
        result.Value.Created.ShouldBe(1);
        var notice = Store.Notifications.ShouldHaveSingleItem();
        notice.RecipientUserId.ShouldBe(Operator);
        notice.Priority.ShouldBe(NotificationPriority.Critical);
        notice.StudentId.ShouldBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DirectoryReturnsAnotherTenantOrStudent_RejectsEntireBatch(bool foreignTenant)
    {
        Directory.ReturnUnfilteredRecipients = true;
        Directory.Students.Add(new(foreignTenant ? OtherOrganization : Organization, OtherGuardian, OtherStudent));
        var result = await Commands.Handle(Event(), Ct);
        result.FirstError.Code.ShouldBe("NOTIFICATION_INVALID_AUDIENCE");
        Store.Notifications.ShouldBeEmpty();
        Store.PendingDispatch.ShouldBeEmpty();
        UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task ForeignOperatorInSos_DoesNotPersistPartialGuardianNotices()
    {
        Directory.Operators.Add(new(OtherOrganization, OtherGuardian));
        var result = await Commands.Handle(Event(new SosRaisedV1(Organization, Trip, Guid.NewGuid(), Guid.NewGuid(), [Student])), Ct);
        result.IsFailure.ShouldBeTrue();
        Store.Notifications.ShouldBeEmpty();
        UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task MissingAuthorizedRecipients_DoesNotBroadcastToAnyoneElse()
    {
        Directory.Students.Clear();
        (await Commands.Handle(Event(), Ct)).Value.Created.ShouldBe(0);
        Store.PendingDispatch.ShouldBeEmpty();
    }

    [Fact]
    public async Task DirectoryUnavailable_PropagatesFailureAndDoesNotCommitOrLosePendingSource()
    {
        Directory.Unavailable = true;
        await Should.ThrowAsync<TimeoutException>(() => Commands.Handle(Event(), Ct));
        UnitOfWork.Commits.ShouldBe(0);
        Store.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExpiredOfflineProximity_IsNotPresentedAsCurrentAlert()
    {
        var result = await Commands.Handle(Event(new VehicleApproachingStopV1(Organization, Trip, Stop, [Student], 3),
            occurredAt: Now.AddMinutes(-6)), Ct);
        result.Value.SkippedReason.ShouldBe("PROXIMITY_EXPIRED");
        Store.PendingDispatch.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProximityFromFinishedTrip_DoesNotCreateLiveAlert()
    {
        Directory.TripActive = false;
        var result = await Commands.Handle(Event(new VehicleApproachingStopV1(Organization, Trip, Stop, [Student], 3)), Ct);
        result.Value.SkippedReason.ShouldBe("TRIP_NOT_ACTIVE");
        Store.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task OldConfirmedBoarding_RemainsHistoricalFactEvenAfterTripEnds()
    {
        Directory.TripActive = false;
        var result = await Commands.Handle(Event(occurredAt: Now.AddHours(-2)), Ct);
        result.Value.Created.ShouldBe(1);
        Store.Notifications.Single().OccurredAt.ShouldBe(Now.AddHours(-2));
    }

    [Theory]
    [InlineData(0, "trip.student-boarded.v1")]
    [InlineData(2, "trip.student-boarded.v1")]
    [InlineData(1, "trip.sos-raised.v1")]
    public async Task UnsupportedOrMismatchedContract_DoesNotCommit(int version, string messageType)
    {
        var command = Event();
        command = command with { Metadata = command.Metadata with { SchemaVersion = version, MessageType = messageType } };
        var result = await Commands.Handle(command, Ct);
        result.FirstError.Code.ShouldBe("NOTIFICATION_UNSUPPORTED_CONTRACT");
        UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task EventWithoutOrganization_IsRejected()
    {
        var result = await Commands.Handle(Event(new StudentBoardedV1(Guid.Empty, Trip, Student, Stop)), Ct);
        result.FirstError.Type.ShouldBe(ErrorType.Validation);
        UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task ReusedSourceIdentityWithDifferentTrip_IsConflict()
    {
        var command = Event();
        await Commands.Handle(command, Ct);
        var altered = command with { Event = new StudentBoardedV1(Organization, Guid.NewGuid(), Student, Stop) };
        var result = await Commands.Handle(altered, Ct);
        result.FirstError.Code.ShouldBe("NOTIFICATION_SOURCE_CONFLICT");
        Store.Notifications.ShouldHaveSingleItem();
        UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task SmallClockSkew_IsAccepted_ButFarFutureEventIsRejected()
    {
        (await Commands.Handle(Event(occurredAt: Now.AddSeconds(10)), Ct)).IsSuccess.ShouldBeTrue();
        var future = await Commands.Handle(Event(occurredAt: Now.AddMinutes(5)), Ct);
        future.FirstError.Code.ShouldBe("NOTIFICATION_EVENT_IN_FUTURE");
        Store.Notifications.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ReturnCompletion_DoesNotDuplicateIndividualDeliveryNotice()
    {
        await Commands.Handle(Event(new StudentAlightedV1(Organization, Trip, Student, Stop)), Ct);
        var result = await Commands.Handle(Event(new TripCompletedV1(Organization, Trip,
            Guid.NewGuid(), TripDirectionV1.Return, [Student])), Ct);
        result.Value.SkippedReason.ShouldBe("RETURN_DELIVERY_REPORTED_BY_ALIGHTING");
        Store.Notifications.ShouldHaveSingleItem().Category.ShouldBe(NotificationCategory.Alighting);
        Store.PendingDispatch.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task CompletionWithoutExplicitDirection_IsRejected()
    {
        var result = await Commands.Handle(Event(new TripCompletedV1(Organization, Trip,
            Guid.NewGuid(), TripDirectionV1.Unknown, [Student])), Ct);
        result.FirstError.Code.ShouldBe("NOTIFICATION_INVALID_EVENT_PAYLOAD");
        UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task CancellationBeforeProcessing_DoesNotCreatePendingWork()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => Commands.Handle(Event(), cancelled.Token));
        Store.PendingDispatch.ShouldBeEmpty();
        UnitOfWork.Commits.ShouldBe(0);
    }
}
