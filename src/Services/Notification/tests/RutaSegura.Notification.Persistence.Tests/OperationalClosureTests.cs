using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Application.QueryServices;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Maintenance;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories;

namespace RutaSegura.Notification.Persistence.Tests;

public sealed class OperationalClosureTests : MySqlTestContext
{
    [MySqlFact]
    public async Task CursorPaginationTranslatesToMySqlAndHandlesTimestampTiesWithoutSkippingRows()
    {
        Guid[] expected;
        await using(var db=OpenContext())
        {
            var notices=Enumerable.Range(0,8).Select(_=>Notice(student:Student)).ToArray();
            expected=notices.Select(n=>n.Id).ToArray(); db.Notifications.AddRange(notices); await db.SaveChangesAsync(Ct);
        }
        await using var services=Services(new(Organization,Guardian,Student));
        var seen=new List<Guid>(); string? cursor=null;
        for(var index=0;index<6;index++)
        {
            await using var scope=services.CreateAsyncScope();
            var page=await scope.ServiceProvider.GetRequiredService<INotificationQueryService>().Handle(new GetMyNotificationPageQuery(cursor,3),Ct);
            page.IsSuccess.ShouldBeTrue(); seen.AddRange(page.Value.Items.Select(n=>n.Id)); cursor=page.Value.NextCursor;
            if(cursor is null) break;
        }
        cursor.ShouldBeNull(); seen.Count.ShouldBe(8); seen.ToHashSet().SetEquals(expected).ShouldBeTrue();
    }

    [MySqlFact]
    public async Task ReplayHistoryAndDeliveryTransitionCommitTogetherAndSurviveANewScope()
    {
        var notice=Notice(student:Student);
        var delivery=NotificationDelivery.Schedule(notice,NotificationChannel.Realtime,Guardian,Now);
        delivery.Fail("CHANNEL_DEPENDENCY_UNAVAILABLE",Now);
        await using(var db=OpenContext()) { db.Notifications.Add(notice); db.Deliveries.Add(delivery); await db.SaveChangesAsync(Ct); }
        await using var services=Services(new(Organization,Guardian,Student));
        var command=new ReplayNotificationDeliveryCommand(delivery.Id,Guid.NewGuid(),"ProviderRecovered");
        await using(var scope=services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<INotificationDeliveryCommandService>().Handle(command,Ct)).Value.Scheduled.ShouldBeTrue();
        await using(var scope=services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<INotificationDeliveryCommandService>().Handle(command,Ct)).Value.Scheduled.ShouldBeFalse();
        await using var verify=OpenContext();
        (await verify.DeliveryReplays.SingleAsync(Ct)).RequestId.ShouldBe(command.RequestId);
        var stored=await verify.Deliveries.SingleAsync(Ct);
        stored.Status.ShouldBe(DeliveryStatus.RetryScheduled); stored.ReplayCount.ShouldBe(1); stored.CycleAttemptCount.ShouldBe(0); stored.AttemptCount.ShouldBe(1);
    }

    [MySqlFact]
    public async Task ConcurrentReplaysCannotBothWinOrLeaveAnUncommittedAuditRecord()
    {
        var notice=Notice(student:Student);
        var delivery=NotificationDelivery.Schedule(notice,NotificationChannel.Realtime,Guardian,Now); delivery.Fail("FCM_UNAVAILABLE",Now);
        await using(var db=OpenContext()) { db.Notifications.Add(notice); db.Deliveries.Add(delivery); await db.SaveChangesAsync(Ct); }
        await using var services=Services(new(Organization,Guardian,Student));
        async Task<bool> Replay()
        {
            await using var scope=services.CreateAsyncScope();
            try { return (await scope.ServiceProvider.GetRequiredService<INotificationDeliveryCommandService>()
                .Handle(new(delivery.Id,Guid.NewGuid(),"ManualReview"),Ct)).IsSuccess; }
            catch(NotificationPersistenceConflictException) { return false; }
        }
        var results=await Task.WhenAll(Replay(),Replay()); results.Count(r=>r).ShouldBe(1);
        await using var verify=OpenContext();
        (await verify.DeliveryReplays.CountAsync(Ct)).ShouldBe(1); (await verify.Deliveries.SingleAsync(Ct)).ReplayCount.ShouldBe(1);
    }

    [MySqlFact]
    public async Task ConcurrentSubscriptionRegistrationsCannotExceedTwentyEnabledDestinations()
    {
        await using(var db=OpenContext())
        {
            for(var i=0;i<19;i++) db.PushSubscriptions.Add(PushSubscription.Register(Organization,Guardian,Guid.NewGuid(),"seed-"+i,PushPlatform.Web,Now));
            await db.SaveChangesAsync(Ct);
        }
        await using var services=Services(new(Organization,Guardian,Student));
        async Task<bool> Register()
        {
            await using var scope=services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<INotificationCommandService>()
                .Handle(new RegisterPushSubscriptionCommand(Guid.NewGuid(),Guid.NewGuid().ToString(),"Web"),Ct)).IsSuccess;
        }
        (await Task.WhenAll(Register(),Register())).Count(r=>r).ShouldBe(1);
        await using var verify=OpenContext(); (await verify.PushSubscriptions.CountAsync(s=>s.IsEnabled,Ct)).ShouldBe(20);
    }

    [MySqlFact]
    public async Task RetentionPreviewAndDeletionPreservePendingUnreadFailedReplayedAndInboxRecords()
    {
        var old=Now.AddDays(-60);
        var retained=new List<Guid>(); Guid removed;
        await using(var db=OpenContext())
        {
            for(var i=0;i<5;i++)
            {
                var notice=Notice(student:Student,createdAt:old); db.Notifications.Add(notice);
                if(i!=1) notice.MarkAsRead(old.AddDays(1));
                var job=NotificationDispatchJob.Schedule(notice); if(i!=3) job.MarkExpanded(old);
                db.DispatchJobs.Add(job);
                var delivery=NotificationDelivery.Schedule(notice,NotificationChannel.Realtime,Guardian,old);
                if(i is 2 or 4) delivery.Fail("CHANNEL_DEPENDENCY_UNAVAILABLE",old);
                if(i==4)
                {
                    delivery.Replay(old,3);
                    db.DeliveryReplays.Add(NotificationDeliveryReplay.Record(Organization,delivery.Id,Guid.NewGuid(),Guardian,"ManualReview",old));
                }
                if(i!=2) delivery.AcceptByProvider("test-receipt",old);
                db.Deliveries.Add(delivery);
                if(i==0) db.ProcessedEvents.Add(Processed(notice.SourceMessageId)); else retained.Add(notice.Id);
            }
            await db.SaveChangesAsync(Ct); removed=(await db.Notifications.SingleAsync(n=>!retained.Contains(n.Id),Ct)).Id;
        }
        await using(var db=OpenContext()) (await new NotificationRetentionStore(db).ExecuteBatchAsync(Now.AddDays(-30),100,true,Ct)).ShouldBe(1);
        await using(var db=OpenContext()) (await db.Notifications.CountAsync(Ct)).ShouldBe(5);
        await using(var db=OpenContext()) (await new NotificationRetentionStore(db).ExecuteBatchAsync(Now.AddDays(-30),100,false,Ct)).ShouldBe(1);
        await using(var db=OpenContext())
        {
            (await db.Notifications.AnyAsync(n=>n.Id==removed,Ct)).ShouldBeFalse();
            (await db.Notifications.CountAsync(Ct)).ShouldBe(4); (await db.DispatchJobs.CountAsync(Ct)).ShouldBe(4); (await db.Deliveries.CountAsync(Ct)).ShouldBe(4);
            (await db.ProcessedEvents.CountAsync(Ct)).ShouldBe(1); (await db.DeliveryReplays.CountAsync(Ct)).ShouldBe(1);
        }
    }

    [MySqlFact]
    public async Task UpgradePreservesExistingDeliveriesAndTheirAlreadyConsumedRetryBudget()
    {
        await using(var db=OpenContext()) await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().First(),Ct);
        var notice=Notice(student:Student);
        var id=Guid.NewGuid();
        await using(var db=OpenContext())
        {
            db.Notifications.Add(notice); await db.SaveChangesAsync(Ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO notification_deliveries (id,organization_id,notification_id,channel,destination_id,status,attempt_count,created_at,last_attempt_at,last_error_code,version) VALUES ({id.ToString()},{Organization.ToString()},{notice.Id.ToString()},{"Realtime"},{Guardian.ToString()},{"Failed"},{5},{Now.UtcDateTime},{Now.UtcDateTime},{"CHANNEL_DEPENDENCY_UNAVAILABLE"},{1L})",Ct);
            await db.Database.MigrateAsync(Ct);
        }
        await using var verify=OpenContext(); var row=await verify.Deliveries.SingleAsync(Ct);
        row.Id.ShouldBe(id); row.AttemptCount.ShouldBe(5); row.CycleAttemptCount.ShouldBe(5); row.ReplayCount.ShouldBe(0);
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(1);
    }
}
