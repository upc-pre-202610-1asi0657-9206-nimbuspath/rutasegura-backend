using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Application.Internal.Authorization;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Internal.Policies;
using RutaSegura.Notification.Application.Internal.Transform;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Application.Internal.Diagnostics;

namespace RutaSegura.Notification.Application.Internal.CommandServices;

internal sealed class NotificationChannelService(INotificationDispatchRepository work, INotificationRepository notifications,
    IPushSubscriptionRepository subscriptions, IAnnouncementRepository announcements, INotificationRecipientDirectory directory,
    ITripNotificationContext trips, IRealtimeNotificationSender realtime, IPushNotificationSender push,
    IUnitOfWork unitOfWork, TimeProvider clock, NotificationChannelPolicy policy) : INotificationChannelService
{
    public async Task ExpandAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await work.GetJobAsync(organizationId, jobId, cancellationToken);
        if (job is null || job.ExpandedAt is not null) return;
        var notice = await notifications.GetAsync(organizationId, job.NotificationId, cancellationToken)
            ?? throw new InvalidOperationException("NOTIFICATION_DISPATCH_ORPHAN");
        var now = clock.GetUtcNow();
        work.Add(NotificationDelivery.Schedule(notice, NotificationChannel.Realtime, notice.RecipientUserId, now));
        if (policy.PushEnabled)
            foreach (var subscription in await subscriptions.ListEnabledAsync(organizationId, notice.RecipientUserId, cancellationToken))
                work.Add(NotificationDelivery.Schedule(notice, NotificationChannel.Push, subscription.Id, now));
        job.MarkExpanded(now);
        await unitOfWork.SaveChangesAsync(cancellationToken); // Destinos + ExpandedAt atomicos.
    }

    public async Task DeliverAsync(Guid organizationId, Guid deliveryId, CancellationToken cancellationToken)
    {
        var delivery = await work.GetDeliveryAsync(organizationId, deliveryId, cancellationToken);
        var now = clock.GetUtcNow();
        if (delivery is null || delivery.Status is not (DeliveryStatus.Pending or DeliveryStatus.RetryScheduled) || delivery.NextAttemptAt > now) return;
        var notice = await notifications.GetAsync(organizationId, delivery.NotificationId, cancellationToken)
            ?? throw new InvalidOperationException("NOTIFICATION_DISPATCH_ORPHAN");
        try
        {
            if (notice.IsExpired(now) || (notice.Category == NotificationCategory.Proximity &&
                !await trips.IsActiveAsync(organizationId, notice.TripId!.Value, cancellationToken)))
                delivery.Skip("NOTIFICATION_EXPIRED");
            else if (!await NotificationAccess.CanReadAsync(new(notice.RecipientUserId, organizationId), notice, directory, announcements, cancellationToken))
                delivery.Skip("NOTIFICATION_ACCESS_REVOKED");
            else
            {
                ChannelSendResult sent;
                if (delivery.Channel == NotificationChannel.Push)
                {
                    var subscription = await subscriptions.GetAsync(organizationId, notice.RecipientUserId, delivery.DestinationId, cancellationToken);
                    if (!policy.PushEnabled || subscription is null || !subscription.IsEnabled)
                    {
                        delivery.Skip("PUSH_SUBSCRIPTION_DISABLED");
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                        NotificationDiagnostics.DeliveryRecorded(delivery.Channel, delivery.Status, notice.OccurredAt, notice.CreatedAt, clock.GetUtcNow());
                        return;
                    }
                    sent = await push.SendAsync(NotificationResultFromEntityAssembler.ToResultFromEntity(notice), subscription.Token, cancellationToken);
                    if (sent.ErrorCode is "FCM_UNREGISTERED" or "FCM_SENDER_ID_MISMATCH") subscription.Disable(clock.GetUtcNow());
                }
                else
                    sent = await realtime.SendAsync(NotificationResultFromEntityAssembler.ToResultFromEntity(notice), cancellationToken);
                now = clock.GetUtcNow();
                if (sent.Accepted && !string.IsNullOrWhiteSpace(sent.ProviderMessageId)) delivery.AcceptByProvider(sent.ProviderMessageId, now);
                else RecordFailure(delivery, sent.ErrorCode ?? "CHANNEL_REJECTED", sent.Retryable, now);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Fallo de autorizacion remota o proveedor: nunca enviar con permisos supuestos ni registrar mensajes sensibles.
            RecordFailure(delivery, "CHANNEL_DEPENDENCY_UNAVAILABLE", true, clock.GetUtcNow());
        }
        // I/O del canal ya termino; esta confirmacion NO acredita lectura ni recepcion por el dispositivo.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        NotificationDiagnostics.DeliveryRecorded(delivery.Channel, delivery.Status, notice.OccurredAt, notice.CreatedAt, clock.GetUtcNow());
    }

    private void RecordFailure(NotificationDelivery delivery, string code, bool retryable, DateTimeOffset now)
    {
        if (!retryable || delivery.AttemptCount + 1 >= policy.MaxAttempts) delivery.Fail(code, now);
        else delivery.Retry(code, now.AddSeconds(Math.Min(300, 5 * Math.Pow(2, delivery.AttemptCount))), now);
    }
}
