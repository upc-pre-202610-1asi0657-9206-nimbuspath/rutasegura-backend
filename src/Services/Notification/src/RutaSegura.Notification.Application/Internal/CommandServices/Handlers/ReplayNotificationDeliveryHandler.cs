using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.Authorization;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Internal.Policies;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Domain.Repositories;

namespace RutaSegura.Notification.Application.Internal.CommandServices.Handlers;

internal sealed class ReplayNotificationDeliveryHandler(INotificationUserContext context, INotificationRecipientDirectory directory,
    INotificationDeliveryAdministrationRepository administration, INotificationDispatchRepository work, INotificationRepository notices,
    IAnnouncementRepository announcements, IPushSubscriptionRepository subscriptions, TimeProvider clock, NotificationReplayPolicy policy)
    : ICommandHandler<ReplayNotificationDeliveryCommand, ReplayNotificationDeliveryResult>
{
    public async Task<Result<ReplayNotificationDeliveryResult>> HandleAsync(ReplayNotificationDeliveryCommand command, CancellationToken ct)
    {
        var current = await NotificationAdministratorAccess.RequireAsync(context, directory, ct);
        if (current.IsFailure) return current.FirstError;
        if (command.RequestId == Guid.Empty || command.DeliveryId == Guid.Empty ||
            command.ReasonCode is not ("ProviderRecovered" or "ConfigurationFixed" or "ManualReview"))
            return Error.Validation("NOTIFICATION_INVALID_REPLAY", "Indica requestId, deliveryId y un motivo permitido.");
        var user = current.Value;
        var previous = await administration.FindReplayAsync(user.OrganizationId, command.RequestId, ct);
        if (previous is not null)
            return previous.DeliveryId == command.DeliveryId && previous.ReasonCode == command.ReasonCode && previous.RequestedByUserId == user.UserId
                ? new ReplayNotificationDeliveryResult(command.DeliveryId, previous.Id, false)
                : Error.Conflict("NOTIFICATION_REPLAY_CONFLICT", "El requestId ya corresponde a otra operacion.");
        var delivery = await work.GetDeliveryAsync(user.OrganizationId, command.DeliveryId, ct);
        if (delivery is null) return Error.NotFound("NOTIFICATION_DELIVERY_NOT_FOUND", "Entrega no disponible.");
        var notice = await notices.GetAsync(user.OrganizationId, delivery.NotificationId, ct);
        if (notice is null) return Error.NotFound("NOTIFICATION_DELIVERY_NOT_FOUND", "Entrega no disponible.");
        var now = clock.GetUtcNow();
        if (delivery.Status != DeliveryStatus.Failed || delivery.ReplayCount >= policy.MaxManualReplays || notice.IsExpired(now))
            return Error.Conflict("NOTIFICATION_REPLAY_NOT_ALLOWED", "La entrega no admite otro reproceso o el aviso caduco.");
        if (!await NotificationAccess.CanReadAsync(new(notice.RecipientUserId, user.OrganizationId), notice, directory, announcements, ct))
            return Error.Conflict("NOTIFICATION_REPLAY_ACCESS_REVOKED", "El destinatario ya no esta autorizado.");
        if (delivery.Channel == NotificationChannel.Push &&
            (await subscriptions.GetAsync(user.OrganizationId, notice.RecipientUserId, delivery.DestinationId, ct)) is not { IsEnabled: true })
            return Error.Conflict("NOTIFICATION_REPLAY_DESTINATION_DISABLED", "La suscripcion ya no esta habilitada.");
        var replay = NotificationDeliveryReplay.Record(user.OrganizationId, delivery.Id, command.RequestId, user.UserId, command.ReasonCode, now);
        delivery.Replay(now, policy.MaxManualReplays);
        administration.Add(replay);
        return new ReplayNotificationDeliveryResult(delivery.Id, replay.Id, true);
    }
}
