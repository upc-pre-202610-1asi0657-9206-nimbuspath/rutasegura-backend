using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.Authorization;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Application.Internal.Transform;

namespace RutaSegura.Notification.Application.Internal.CommandServices.Handlers;

internal sealed class RegisterPushSubscriptionHandler(IPushSubscriptionRepository subscriptions,
    INotificationUserContext context, TimeProvider clock) : ICommandHandler<RegisterPushSubscriptionCommand, PushSubscriptionResult>
{
    public async Task<Result<PushSubscriptionResult>> HandleAsync(RegisterPushSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var current = NotificationAccess.Current(context);
        if (current.IsFailure) return current.FirstError;
        if (command.InstallationId == Guid.Empty || string.IsNullOrWhiteSpace(command.Token) ||
            command.Token.Length > PushSubscription.MaxTokenLength || command.Token.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ||
            !Enum.GetNames<PushPlatform>().Contains(command.Platform, StringComparer.OrdinalIgnoreCase) ||
            !Enum.TryParse<PushPlatform>(command.Platform, true, out var platform) || !Enum.IsDefined(platform))
            return Error.Validation("PUSH_INVALID_SUBSCRIPTION", "Se requiere instalacion, token y plataforma Web, Android o Ios validos.");
        var user = current.Value;
        var existing = await subscriptions.FindByInstallationAsync(user.OrganizationId, user.UserId, command.InstallationId, cancellationToken);
        if (existing is null)
        {
            if ((await subscriptions.ListEnabledAsync(user.OrganizationId, user.UserId, cancellationToken)).Count >= 20)
                return Error.Conflict("PUSH_SUBSCRIPTION_LIMIT", "Desactiva una suscripcion antes de registrar otra.");
            existing = PushSubscription.Register(user.OrganizationId, user.UserId, command.InstallationId, command.Token, platform, clock.GetUtcNow());
            subscriptions.Add(existing);
        }
        else existing.Refresh(command.Token, platform, clock.GetUtcNow());
        return PushSubscriptionResultFromEntityAssembler.ToResultFromEntity(existing);
    }
}

internal sealed class DisablePushSubscriptionHandler(IPushSubscriptionRepository subscriptions, INotificationUserContext context,
    TimeProvider clock) : ICommandHandler<DisablePushSubscriptionCommand, PushSubscriptionResult>
{
    public async Task<Result<PushSubscriptionResult>> HandleAsync(DisablePushSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var current = NotificationAccess.Current(context);
        if (current.IsFailure) return current.FirstError;
        var user = current.Value;
        var subscription = await subscriptions.GetAsync(user.OrganizationId, user.UserId, command.SubscriptionId, cancellationToken);
        if (subscription is null) return Error.NotFound("PUSH_SUBSCRIPTION_NOT_FOUND", "La suscripcion no esta disponible para este usuario.");
        if (subscription.IsEnabled) subscription.Disable(clock.GetUtcNow());
        return PushSubscriptionResultFromEntityAssembler.ToResultFromEntity(subscription);
    }
}

