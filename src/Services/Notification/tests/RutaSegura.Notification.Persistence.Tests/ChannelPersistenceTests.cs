using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Application.DependencyInjection;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Internal.Policies;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Infrastructure.Channels;
using RutaSegura.Notification.Infrastructure.DependencyInjection;
using RutaSegura.Notification.Domain.Repositories;

namespace RutaSegura.Notification.Persistence.Tests;

public sealed class ChannelPersistenceTests : MySqlTestContext
{
    [MySqlFact]
    public async Task CriticalAndHighPriorityWorkIsSelectedAheadOfOlderOrdinaryNotices()
    {
        await using var db = OpenContext();
        foreach (var category in new[] { NotificationCategory.Boarding, NotificationCategory.Incident, NotificationCategory.Sos })
        {
            var notice = RutaSegura.Notification.Domain.Model.Aggregates.Notification.Create(Organization, Guardian, Guid.NewGuid(), "test.event", "test",
                category, NotificationContent.Create("Aviso", "Contenido"), Now, Now, Trip, Student);
            db.Notifications.Add(notice);
            db.DispatchJobs.Add(NotificationDispatchJob.Schedule(notice));
            db.Deliveries.Add(NotificationDelivery.Schedule(notice, NotificationChannel.Realtime, Guardian, Now));
        }
        await db.SaveChangesAsync(Ct);
        var repository = new RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories.NotificationDispatchRepository(db);
        var jobs = await repository.ListPendingJobsAsync(3, Ct);
        var deliveries = await repository.ListDueDeliveriesAsync(Now, 3, Ct);
        (await db.Notifications.SingleAsync(n => n.Id == jobs[0].NotificationId, Ct)).Category.ShouldBe(NotificationCategory.Sos);
        (await db.Notifications.SingleAsync(n => n.Id == deliveries[0].NotificationId, Ct)).Category.ShouldBe(NotificationCategory.Sos);
    }

    private readonly ChannelClock _clock = new();

    private ServiceProvider Provider(RecipientDirectory directory, Sender sender)
    {
        _clock.Now = Now;
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddNotificationPersistence(ConnectionString, TestSecrets.EncryptionKey);
        services.AddSingleton<INotificationRecipientDirectory>(directory);
        services.AddSingleton<ITripNotificationContext>(directory);
        services.AddSingleton<IRealtimeNotificationSender>(sender);
        services.AddSingleton<IPushNotificationSender>(sender);
        services.AddNotificationChannelApplication(new NotificationChannelPolicy(true));
        services.AddSingleton(new MySqlDispatchLock(ConnectionString));
        services.AddSingleton(new NotificationChannelOptions());
        services.AddSingleton<NotificationChannelWorker>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private async Task<Guid> Seed(bool proximity = false)
    {
        await using var db = OpenContext();
        var notice = proximity
            ? RutaSegura.Notification.Domain.Model.Aggregates.Notification.Create(Organization, Guardian, Guid.NewGuid(), "trip.vehicle-approaching-stop.v1", "test",
                NotificationCategory.Proximity, NotificationContent.Create("Aviso", "Se aproxima"), Now, Now, Trip, Student, expiresAt: Now.AddMinutes(5))
            : Notice(student: Student);
        db.Notifications.Add(notice);
        db.DispatchJobs.Add(NotificationDispatchJob.Schedule(notice));
        db.PushSubscriptions.Add(PushSubscription.Register(Organization, Guardian, Guid.NewGuid(), "original-token", PushPlatform.Web, Now));
        await db.SaveChangesAsync(Ct);
        return notice.Id;
    }

    [MySqlFact]
    public async Task ConcurrentWorkersExpandAndSendOncePerChannelAndKeepNoticeUnread()
    {
        await Seed();
        var sender = new Sender { Delay = true };
        await using var services = Provider(new(Organization, Guardian, Student), sender);
        var worker = services.GetRequiredService<NotificationChannelWorker>();
        await Task.WhenAll(worker.ProcessBatchAsync(Ct), worker.ProcessBatchAsync(Ct));
        await worker.ProcessBatchAsync(Ct);
        sender.Realtime.Count.ShouldBe(1);
        sender.Push.Count.ShouldBe(1);
        await using var verify = OpenContext();
        (await verify.DispatchJobs.SingleAsync(Ct)).ExpandedAt.ShouldNotBeNull();
        (await verify.Deliveries.CountAsync(d => d.Status == DeliveryStatus.ProviderAccepted, Ct)).ShouldBe(2);
        (await verify.Notifications.SingleAsync(Ct)).ReadAt.ShouldBeNull();
    }

    [MySqlFact]
    public async Task RevokedLinkSkipsBothChannelsWithoutCallingProviders()
    {
        await Seed();
        var sender = new Sender();
        await using var services = Provider(new(Organization, Guardian, Student) { LinkRevoked = true }, sender);
        await services.GetRequiredService<NotificationChannelWorker>().ProcessBatchAsync(Ct);
        sender.Realtime.ShouldBeEmpty(); sender.Push.ShouldBeEmpty();
        await using var verify = OpenContext();
        (await verify.Deliveries.CountAsync(d => d.Status == DeliveryStatus.Skipped && d.LastErrorCode == "NOTIFICATION_ACCESS_REVOKED", Ct)).ShouldBe(2);
    }

    [MySqlFact]
    public async Task DirectoryOutageRetriesWithoutSendingThenRecovers()
    {
        await Seed();
        var directory = new RecipientDirectory(Organization, Guardian, Student) { Unavailable = true };
        var sender = new Sender();
        await using var services = Provider(directory, sender);
        var worker = services.GetRequiredService<NotificationChannelWorker>();
        await worker.ProcessBatchAsync(Ct);
        sender.Realtime.ShouldBeEmpty(); sender.Push.ShouldBeEmpty();
        await using (var verify = OpenContext())
            (await verify.Deliveries.CountAsync(d => d.Status == DeliveryStatus.RetryScheduled && d.AttemptCount == 1, Ct)).ShouldBe(2);
        directory.Unavailable = false;
        _clock.Now = Now.AddSeconds(5);
        await worker.ProcessBatchAsync(Ct);
        sender.Realtime.Count.ShouldBe(1); sender.Push.Count.ShouldBe(1);
    }

    [MySqlFact]
    public async Task UnregisteredFcmDestinationIsDisabledButInboxAndRealtimeSurvive()
    {
        await Seed();
        var sender = new Sender { PushResult = new(false, false, null, "FCM_UNREGISTERED") };
        await using var services = Provider(new(Organization, Guardian, Student), sender);
        await services.GetRequiredService<NotificationChannelWorker>().ProcessBatchAsync(Ct);
        await using var verify = OpenContext();
        (await verify.PushSubscriptions.SingleAsync(Ct)).IsEnabled.ShouldBeFalse();
        (await verify.Deliveries.SingleAsync(d => d.Channel == NotificationChannel.Push, Ct)).Status.ShouldBe(DeliveryStatus.Failed);
        (await verify.Deliveries.SingleAsync(d => d.Channel == NotificationChannel.Realtime, Ct)).Status.ShouldBe(DeliveryStatus.ProviderAccepted);
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(1);
    }

    [MySqlFact]
    public async Task DisabledSubscriptionIsRecheckedAfterExpansion()
    {
        await Seed();
        var sender = new Sender();
        await using var services = Provider(new(Organization, Guardian, Student), sender);
        await using (var scope = services.CreateAsyncScope())
        {
            await using var db = OpenContext();
            var job = await db.DispatchJobs.SingleAsync(Ct);
            await scope.ServiceProvider.GetRequiredService<INotificationChannelService>().ExpandAsync(Organization, job.Id, Ct);
            (await db.PushSubscriptions.SingleAsync(Ct)).Disable(Now);
            await db.SaveChangesAsync(Ct);
        }
        await services.GetRequiredService<NotificationChannelWorker>().ProcessBatchAsync(Ct);
        sender.Push.ShouldBeEmpty();
        await using var verify = OpenContext();
        (await verify.Deliveries.SingleAsync(d => d.Channel == NotificationChannel.Push, Ct)).Status.ShouldBe(DeliveryStatus.Skipped);
    }

    [MySqlFact]
    public async Task RetryBudgetIsDurableAndFinite()
    {
        await Seed();
        var sender = new Sender { PushResult = new(false, true, null, "FCM_UNAVAILABLE") };
        await using var services = Provider(new(Organization, Guardian, Student), sender);
        var worker = services.GetRequiredService<NotificationChannelWorker>();
        for (var i = 0; i < 6; i++)
        {
            await worker.ProcessBatchAsync(Ct);
            _clock.Now = _clock.Now.AddMinutes(10);
        }
        sender.Push.Count.ShouldBe(5);
        await using var verify = OpenContext();
        var failed = await verify.Deliveries.SingleAsync(d => d.Channel == NotificationChannel.Push, Ct);
        failed.Status.ShouldBe(DeliveryStatus.Failed); failed.AttemptCount.ShouldBe(5);
    }

    [MySqlFact]
    public async Task FcmOutageSurvivesAWorkerRestart_AndRetriesOnlyPushWithoutDuplicatingRealtime()
    {
        await Seed();
        var unavailable = new Sender { PushResult = new(false, true, null, "FCM_UNAVAILABLE") };
        await using (var first = Provider(new(Organization, Guardian, Student), unavailable))
            await first.GetRequiredService<NotificationChannelWorker>().ProcessBatchAsync(Ct);
        unavailable.Realtime.Count.ShouldBe(1);
        await using (var db = OpenContext())
        {
            var push = await db.Deliveries.SingleAsync(d => d.Channel == NotificationChannel.Push, Ct);
            push.Status.ShouldBe(DeliveryStatus.RetryScheduled);
            push.AttemptCount.ShouldBe(1);
            (await db.Notifications.CountAsync(Ct)).ShouldBe(1);
        }
        // Nuevos servicios y contextos contra la misma base simulan un proceso reiniciado, sin estado en memoria.
        var recovered = new Sender();
        await using (var second = Provider(new(Organization, Guardian, Student), recovered))
        {
            _clock.Now = Now.AddSeconds(5);
            await second.GetRequiredService<NotificationChannelWorker>().ProcessBatchAsync(Ct);
            await second.GetRequiredService<NotificationChannelWorker>().ProcessBatchAsync(Ct);
        }
        recovered.Push.Count.ShouldBe(1);
        recovered.Realtime.ShouldBeEmpty();
        await using var verify = OpenContext();
        (await verify.Deliveries.CountAsync(d => d.Status == DeliveryStatus.ProviderAccepted, Ct)).ShouldBe(2);
        (await verify.Deliveries.SingleAsync(d => d.Channel == NotificationChannel.Push, Ct)).AttemptCount.ShouldBe(2);
        (await verify.Notifications.SingleAsync(Ct)).ReadAt.ShouldBeNull();
    }

    [MySqlFact]
    public async Task ExpiredProximityIsNotSentAfterADelayedRestart()
    {
        await Seed(proximity: true);
        var sender = new Sender();
        await using var services = Provider(new(Organization, Guardian, Student), sender);
        _clock.Now = Now.AddMinutes(6);
        await services.GetRequiredService<NotificationChannelWorker>().ProcessBatchAsync(Ct);
        sender.Realtime.ShouldBeEmpty(); sender.Push.ShouldBeEmpty();
        await using var verify = OpenContext();
        (await verify.Deliveries.CountAsync(d => d.Status == DeliveryStatus.Skipped && d.LastErrorCode == "NOTIFICATION_EXPIRED", Ct)).ShouldBe(2);
    }

    [MySqlFact]
    public async Task DedicatedSessionLockExcludesOtherWorkersAndCanBeReacquiredAfterRelease()
    {
        var locks = new MySqlDispatchLock(ConnectionString);
        var id = Guid.NewGuid();
        var first = await locks.TryAcquireAsync(id, Ct);
        first.ShouldNotBeNull();
        try { (await locks.TryAcquireAsync(id, Ct)).ShouldBeNull(); }
        finally { await first.DisposeAsync(); }
        await using var reacquired = await locks.TryAcquireAsync(id, Ct);
        reacquired.ShouldNotBeNull();
    }

    private sealed class ChannelClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Sender : IRealtimeNotificationSender, IPushNotificationSender
    {
        public ConcurrentBag<Guid> Realtime { get; } = [];
        public ConcurrentBag<string> Push { get; } = [];
        public bool Delay { get; init; }
        public ChannelSendResult PushResult { get; init; } = new(true, false, "fcm:test", null);
        public async Task<ChannelSendResult> SendAsync(NotificationResult notification, CancellationToken ct)
        {
            Realtime.Add(notification.Id);
            if (Delay) await Task.Delay(50, ct);
            return new(true, false, "signalr:test", null);
        }
        public async Task<ChannelSendResult> SendAsync(NotificationResult notification, string registrationToken, CancellationToken ct)
        {
            Push.Add(registrationToken);
            if (Delay) await Task.Delay(50, ct);
            return PushResult;
        }
    }
}
