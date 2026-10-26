using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Application.Internal.OutboundServices;

namespace RutaSegura.Notification.Application.Internal.CommandServices;

/// <summary>Entrada a los comandos. Conserva Logging, Validation y Transaction del Dispatcher compartido.</summary>
internal sealed class NotificationCommandService(IDispatcher dispatcher, INotificationUserContext identity,
    INotificationSubscriptionLock? subscriptionLock = null) : INotificationCommandService
{
    public Task<Result<PushSubscriptionResult>> Handle(RegisterPushSubscriptionCommand command, CancellationToken cancellationToken = default) =>
        MutateSubscriptionAsync(() => dispatcher.SendAsync(command, cancellationToken), cancellationToken);
    public Task<Result<PushSubscriptionResult>> Handle(DisablePushSubscriptionCommand command, CancellationToken cancellationToken = default) =>
        MutateSubscriptionAsync(() => dispatcher.SendAsync(command, cancellationToken), cancellationToken);

    private async Task<Result<PushSubscriptionResult>> MutateSubscriptionAsync(Func<Task<Result<PushSubscriptionResult>>> mutate, CancellationToken ct)
    {
        if (subscriptionLock is null || identity.Current is not { } user) return await mutate();
        await using var lease = await subscriptionLock.TryAcquireAsync(user.OrganizationId, user.UserId, ct);
        if (lease is null) return Error.Conflict("PUSH_SUBSCRIPTION_BUSY", "Hay otra actualizacion en curso; reintenta con la misma instalacion.");
        return await mutate(); // El lock permanece hasta terminar SaveChanges del Dispatcher.
    }

    public Task<Result<ReceiveTripEventResult>> Handle(ReceiveTripEventCommand command, CancellationToken cancellationToken = default) =>
        dispatcher.SendAsync(command, cancellationToken);

    public Task<Result<CreateAnnouncementResult>> Handle(CreateAnnouncementCommand command, CancellationToken cancellationToken = default) =>
        dispatcher.SendAsync(command, cancellationToken);

    public Task<Result<NotificationResult>> Handle(MarkNotificationAsReadCommand command, CancellationToken cancellationToken = default) =>
        dispatcher.SendAsync(command, cancellationToken);
}
