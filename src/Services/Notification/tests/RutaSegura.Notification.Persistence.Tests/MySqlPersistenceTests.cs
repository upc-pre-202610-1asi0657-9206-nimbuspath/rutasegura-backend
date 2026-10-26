using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Application.Contracts.V1;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories;

namespace RutaSegura.Notification.Persistence.Tests;

public sealed class MySqlPersistenceTests : MySqlTestContext
{
    [MySqlFact]
    public async Task MigrationsCanBeAppliedTwice_WithoutDroppingExistingData()
    {
        await using var context = OpenContext();
        context.Notifications.Add(Notice());
        await context.SaveChangesAsync(Ct);
        await context.Database.MigrateAsync(Ct);
        (await context.Notifications.CountAsync(Ct)).ShouldBe(1);
        (await context.Database.GetAppliedMigrationsAsync(Ct)).Count().ShouldBe(2);
        context.Database.HasPendingModelChanges().ShouldBeFalse();
        await context.Database.OpenConnectionAsync(Ct);
        await using var query = context.Database.GetDbConnection().CreateCommand();
        query.CommandText = "SELECT VERSION()";
        // Version del Compose y los scripts actuales; el pull migro la infraestructura de 8.4 a 8.0.
        ((string)(await query.ExecuteScalarAsync(Ct))!).ShouldStartWith("8.0.");
        query.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name LIKE 'notification_%' AND engine = 'InnoDB'";
        Convert.ToInt32(await query.ExecuteScalarAsync(Ct)).ShouldBe(7);
    }

    [MySqlFact]
    public async Task UnitOfWorkCommitsNoticeInboxAndPendingDispatchTogether()
    {
        var notice = Notice(student: Student);
        await using (var context = OpenContext())
        {
            new NotificationRepository(context).Add(notice);
            new ProcessedNotificationEventRepository(context).Add(Processed(notice.SourceMessageId));
            new NotificationDispatchRepository(context).Enqueue(notice);
            await new NotificationUnitOfWork(context).SaveChangesAsync(Ct);
        }
        await using var verify = OpenContext();
        (await verify.Notifications.SingleAsync(Ct)).Content.Title.ShouldBe("Aviso");
        (await verify.ProcessedEvents.SingleAsync(Ct)).MessageId.ShouldBe(notice.SourceMessageId);
        (await verify.DispatchJobs.SingleAsync(Ct)).NotificationId.ShouldBe(notice.Id);
    }

    [MySqlFact]
    public async Task DuplicateInboxRollsBackWholeBatch_IncludingAnnouncementAndDispatch()
    {
        var source = Guid.NewGuid();
        await using (var seed = OpenContext()) { seed.ProcessedEvents.Add(Processed(source)); await seed.SaveChangesAsync(Ct); }
        await using (var context = OpenContext())
        {
            var announcement = Announcement.Create(Organization, Guardian, Guid.NewGuid(), null, NotificationContent.Create("Aviso", "Mensaje"), Now);
            context.Announcements.Add(announcement);
            var notice = Notice(source, announcement: announcement.Id);
            context.Notifications.Add(notice);
            context.DispatchJobs.Add(NotificationDispatchJob.Schedule(notice));
            context.ProcessedEvents.Add(Processed(source));
            var error = await Should.ThrowAsync<NotificationPersistenceConflictException>(() => new NotificationUnitOfWork(context).SaveChangesAsync(Ct));
            error.Code.ShouldBe("NOTIFICATION_DUPLICATE_KEY");
        }
        await using var verify = OpenContext();
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(0);
        (await verify.Announcements.CountAsync(Ct)).ShouldBe(0);
        (await verify.DispatchJobs.CountAsync(Ct)).ShouldBe(0);
        (await verify.ProcessedEvents.CountAsync(Ct)).ShouldBe(1);
    }

    [MySqlFact]
    public async Task DuplicateNoticeWithoutStudent_IsBlockedByNonNullSubject()
    {
        var source = Guid.NewGuid();
        await using var context = OpenContext();
        context.Notifications.AddRange(Notice(source), Notice(source));
        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
        await using var verify = OpenContext();
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(0);
    }

    [MySqlFact]
    public async Task DuplicateNoticeForStudent_IsBlocked_ButDifferentStudentsAreAllowed()
    {
        var source = Guid.NewGuid();
        await using (var context = OpenContext())
        {
            context.Notifications.AddRange(Notice(source, Student), Notice(source, Guid.NewGuid()));
            await context.SaveChangesAsync(Ct);
        }
        await using (var duplicate = OpenContext())
        {
            duplicate.Notifications.Add(Notice(source, Student));
            await Should.ThrowAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(Ct));
        }
        await using var verify = OpenContext();
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(2);
    }

    [MySqlFact]
    public async Task AnnouncementRequestIsUniqueWithinOrganization()
    {
        var request = Guid.NewGuid();
        await using var context = OpenContext();
        context.Announcements.AddRange(
            Announcement.Create(Organization, Guardian, request, null, NotificationContent.Create("A", "B"), Now),
            Announcement.Create(Organization, Guardian, request, null, NotificationContent.Create("A", "B"), Now));
        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }

    [MySqlFact]
    public async Task InboxIdentityIsConsumerAndMessage_NotTenant()
    {
        var source = Guid.NewGuid();
        await using var context = OpenContext();
        context.ProcessedEvents.Add(Processed(source));
        context.ProcessedEvents.Add(ProcessedNotificationEvent.Record("notification.trip-events.v1", source,
            OtherOrganization, "trip.student-boarded.v1", new string('B', 64), Now, Now, 1, null));
        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }

    [MySqlFact]
    public async Task PushTokensAreEncryptedAtRest_AndCanBeDisabledWithoutRemovingInbox()
    {
        const string token = "test-fcm-token-not-a-real-credential";
        var subscription = PushSubscription.Register(Organization, Guardian, Guid.NewGuid(), token, PushPlatform.Web, Now);
        await using (var context = OpenContext())
        {
            new PushSubscriptionRepository(context).Add(subscription);
            context.Notifications.Add(Notice());
            await context.SaveChangesAsync(Ct);
            await context.Database.OpenConnectionAsync(Ct);
            await using var query = context.Database.GetDbConnection().CreateCommand();
            query.CommandText = "SELECT token_ciphertext FROM notification_push_subscriptions LIMIT 1";
            var ciphertext = (string)(await query.ExecuteScalarAsync(Ct))!;
            ciphertext.ShouldStartWith("v1:");
            ciphertext.ShouldNotContain(token);
        }
        await using (var context = OpenContext())
        {
            var repository = new PushSubscriptionRepository(context);
            var loaded = await repository.GetAsync(Organization, Guardian, subscription.Id, Ct);
            loaded.ShouldNotBeNull().Token.ShouldBe(token);
            (await repository.GetAsync(OtherOrganization, Guardian, subscription.Id, Ct)).ShouldBeNull();
            loaded.Disable(Now.AddMinutes(1));
            await context.SaveChangesAsync(Ct);
        }
        await using var verify = OpenContext();
        (await new PushSubscriptionRepository(verify).ListEnabledAsync(Organization, Guardian, Ct)).ShouldBeEmpty();
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(1);
    }

    [MySqlFact]
    public async Task SamePushTokenCannotBeBoundToTwoUsers()
    {
        await using var context = OpenContext();
        context.PushSubscriptions.AddRange(
            PushSubscription.Register(Organization, Guardian, Guid.NewGuid(), "same-token", PushPlatform.Web, Now),
            PushSubscription.Register(OtherOrganization, Guid.NewGuid(), Guid.NewGuid(), "same-token", PushPlatform.Android, Now));
        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }

    [MySqlFact]
    public async Task SameInstallationCannotBeBoundTwiceInOneOrganization()
    {
        var installation = Guid.NewGuid();
        await using var context = OpenContext();
        context.PushSubscriptions.AddRange(
            PushSubscription.Register(Organization, Guardian, installation, "first-token", PushPlatform.Web, Now),
            PushSubscription.Register(Organization, Guid.NewGuid(), installation, "second-token", PushPlatform.Web, Now));
        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }

    [MySqlFact]
    public async Task DeliveryRetriesPersist_AndOnlyDueWorkIsSelected()
    {
        var notice = Notice();
        var retry = NotificationDelivery.Schedule(notice, NotificationChannel.Push, Guid.NewGuid(), Now);
        retry.Retry("TEMPORARY_UNAVAILABLE", Now.AddMinutes(2), Now);
        var accepted = NotificationDelivery.Schedule(notice, NotificationChannel.Realtime, Guardian, Now);
        accepted.AcceptByProvider("channel-accepted", Now);
        await using (var context = OpenContext())
        {
            context.Notifications.Add(notice);
            context.Deliveries.AddRange(retry, accepted);
            await context.SaveChangesAsync(Ct);
        }
        await using var verify = OpenContext();
        var repository = new NotificationDispatchRepository(verify);
        (await repository.ListDueDeliveriesAsync(Now.AddMinutes(1), 10, Ct)).ShouldBeEmpty();
        var due = (await repository.ListDueDeliveriesAsync(Now.AddMinutes(2), 10, Ct)).ShouldHaveSingleItem();
        due.Id.ShouldBe(retry.Id);
        due.AttemptCount.ShouldBe(1);
        due.LastErrorCode.ShouldBe("TEMPORARY_UNAVAILABLE");
        (await verify.Notifications.SingleAsync(Ct)).ReadAt.ShouldBeNull();
    }

    [MySqlFact]
    public async Task DeliveryAndPendingJobHaveDatabaseUniqueness()
    {
        var notice = Notice();
        await using (var seed = OpenContext()) { seed.Notifications.Add(notice); await seed.SaveChangesAsync(Ct); }
        await using (var jobs = OpenContext())
        {
            jobs.DispatchJobs.AddRange(NotificationDispatchJob.Schedule(notice), NotificationDispatchJob.Schedule(notice));
            await Should.ThrowAsync<DbUpdateException>(() => jobs.SaveChangesAsync(Ct));
        }
        await using var deliveries = OpenContext();
        var destination = Guid.NewGuid();
        deliveries.Deliveries.AddRange(NotificationDelivery.Schedule(notice, NotificationChannel.Push, destination, Now),
            NotificationDelivery.Schedule(notice, NotificationChannel.Push, destination, Now));
        await Should.ThrowAsync<DbUpdateException>(() => deliveries.SaveChangesAsync(Ct));
    }

    [MySqlFact]
    public async Task ForeignOrganizationCannotAttachDeliveryToNotice()
    {
        var notice = Notice();
        await using var context = OpenContext();
        context.Notifications.Add(notice);
        await context.SaveChangesAsync(Ct);
        var delivery = NotificationDelivery.Schedule(notice, NotificationChannel.Push, Guid.NewGuid(), Now);
        context.Deliveries.Add(delivery);
        context.Entry(delivery).Property(d => d.OrganizationId).CurrentValue = OtherOrganization;
        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }

    [MySqlFact]
    public async Task ConcurrentReadUpdatesAreDetected_AndDoNotOverwriteFirstRead()
    {
        var notice = Notice();
        await using (var seed = OpenContext()) { seed.Notifications.Add(notice); await seed.SaveChangesAsync(Ct); }
        await using var first = OpenContext();
        await using var second = OpenContext();
        var a = (await new NotificationRepository(first).GetAsync(Organization, notice.Id, Ct))!;
        var b = (await new NotificationRepository(second).GetAsync(Organization, notice.Id, Ct))!;
        a.MarkAsRead(Now.AddMinutes(1));
        b.MarkAsRead(Now.AddMinutes(2));
        await new NotificationUnitOfWork(first).SaveChangesAsync(Ct);
        var conflict = await Should.ThrowAsync<NotificationPersistenceConflictException>(() => new NotificationUnitOfWork(second).SaveChangesAsync(Ct));
        conflict.Code.ShouldBe("NOTIFICATION_CONCURRENT_UPDATE");
        await using var verify = OpenContext();
        (await verify.Notifications.SingleAsync(Ct)).ReadAt.ShouldBe(Now.AddMinutes(1));
    }

    [MySqlFact]
    public async Task InboxAppliesTenantAndCurrentStudentLinksBeforePaging()
    {
        var allowed = Notice(student: Student);
        await using (var seed = OpenContext())
        {
            seed.Notifications.AddRange(allowed, Notice(student: Guid.NewGuid(), createdAt: Now.AddMinutes(1)),
                Notice(organization: OtherOrganization, createdAt: Now.AddMinutes(2)));
            await seed.SaveChangesAsync(Ct);
        }
        await using var context = OpenContext();
        var repository = new NotificationRepository(context);
        var page = await repository.ListAsync(new InboxFilter(Organization, Guardian, [Student], 0, 1, false), Ct);
        page.ShouldHaveSingleItem().Id.ShouldBe(allowed.Id);
        (await repository.GetAsync(OtherOrganization, allowed.Id, Ct)).ShouldBeNull();
    }

    [MySqlFact]
    public async Task DispatcherPersistsInboxAndRecognizesRetryEvenWhenRosterIsUnavailable()
    {
        var directory = new RecipientDirectory(Organization, Guardian, Student);
        await using var services = Services(directory);
        var command = new ReceiveTripEventCommand(new(Guid.NewGuid(), "trip.student-boarded.v1", 1, Now, "integration-test"),
            new StudentBoardedV1(Organization, Trip, Student, Guid.NewGuid()));
        await using (var scope = services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<INotificationCommandService>().Handle(command, Ct)).Value.Created.ShouldBe(1);
        directory.Unavailable = true;
        await using (var scope = services.CreateAsyncScope())
        {
            var duplicate = await scope.ServiceProvider.GetRequiredService<INotificationCommandService>().Handle(command, Ct);
            duplicate.Value.Created.ShouldBe(0);
            duplicate.Value.Duplicates.ShouldBe(1);
        }
        await using var verify = OpenContext();
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(1);
        (await verify.ProcessedEvents.CountAsync(Ct)).ShouldBe(1);
        (await verify.DispatchJobs.CountAsync(Ct)).ShouldBe(1);
    }

    [MySqlFact]
    public async Task FailedUseCaseDoesNotMarkSourceEventAsProcessed()
    {
        var directory = new RecipientDirectory(Organization, Guardian, Student) { Unavailable = true };
        await using var services = Services(directory);
        await using var scope = services.CreateAsyncScope();
        var command = new ReceiveTripEventCommand(new(Guid.NewGuid(), "trip.student-boarded.v1", 1, Now, "integration-test"),
            new StudentBoardedV1(Organization, Trip, Student, Guid.NewGuid()));
        await Should.ThrowAsync<TimeoutException>(() => scope.ServiceProvider.GetRequiredService<INotificationCommandService>().Handle(command, Ct));
        await using var verify = OpenContext();
        (await verify.ProcessedEvents.CountAsync(Ct)).ShouldBe(0);
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(0);
    }

    [MySqlFact]
    public async Task ConcurrentSourceRetriesHaveOneDurableEffect_AndCanBeReevaluatedAfterConflict()
    {
        var directory = new RecipientDirectory(Organization, Guardian, Student);
        await using var services = Services(directory);
        var command = new ReceiveTripEventCommand(new(Guid.NewGuid(), "trip.student-boarded.v1", 1, Now, "concurrent-test"),
            new StudentBoardedV1(Organization, Trip, Student, Guid.NewGuid()));
        async Task<bool> Attempt()
        {
            await using var scope = services.CreateAsyncScope();
            try { return (await scope.ServiceProvider.GetRequiredService<INotificationCommandService>().Handle(command, Ct)).IsSuccess; }
            catch (NotificationPersistenceConflictException ex) when (ex.Code == "NOTIFICATION_DUPLICATE_KEY") { return false; }
        }
        var results = await Task.WhenAll(Attempt(), Attempt());
        results.ShouldContain(true);
        (await Attempt()).ShouldBeTrue();
        await using var verify = OpenContext();
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(1);
        (await verify.DispatchJobs.CountAsync(Ct)).ShouldBe(1);
        (await verify.ProcessedEvents.CountAsync(Ct)).ShouldBe(1);
    }

    [MySqlFact]
    public async Task SubjectCheckRejectsInconsistentDeduplicationKey()
    {
        await using var context = OpenContext();
        var notice = Notice();
        context.Notifications.Add(notice);
        context.Entry(notice).Property(n => n.SubjectId).CurrentValue = Guid.NewGuid();
        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }

    [MySqlFact]
    public async Task SameMessageWithChangedStopIsConflict_EvenIfNotificationTextIsUnchanged()
    {
        await using var services = Services(new RecipientDirectory(Organization, Guardian, Student));
        var command = new ReceiveTripEventCommand(new(Guid.NewGuid(), "trip.student-boarded.v1", 1, Now, "integration-test"),
            new StudentBoardedV1(Organization, Trip, Student, Guid.NewGuid()));
        await using (var scope = services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<INotificationCommandService>().Handle(command, Ct)).IsSuccess.ShouldBeTrue();
        await using (var scope = services.CreateAsyncScope())
        {
            var changed = command with { Event = new StudentBoardedV1(Organization, Trip, Student, Guid.NewGuid()) };
            var conflict = await scope.ServiceProvider.GetRequiredService<INotificationCommandService>().Handle(changed, Ct);
            conflict.FirstError.Code.ShouldBe("NOTIFICATION_SOURCE_CONFLICT");
        }
        await using var verify = OpenContext();
        (await verify.ProcessedEvents.CountAsync(Ct)).ShouldBe(1);
    }
}
