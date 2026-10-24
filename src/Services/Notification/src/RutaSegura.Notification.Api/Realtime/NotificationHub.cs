using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RutaSegura.Notification.Api.Security;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Api.Realtime;

public sealed record NotificationHint(Guid NotificationId, DateTimeOffset CreatedAt);

[Authorize(Policy = "notification-user")]
public sealed class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Context.User is null || !NotificationIdentity.Valid(Context.User)) { Context.Abort(); return; }
        await Groups.AddToGroupAsync(Context.ConnectionId,
            NotificationIdentity.Group(NotificationIdentity.OrganizationId(Context.User), NotificationIdentity.UserId(Context.User)), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
    // No existen metodos JoinGroup/Subscribe con ids elegidos por el cliente.
}

public sealed class SignalRNotificationSender(IHubContext<NotificationHub> hub) : IRealtimeNotificationSender
{
    public async Task<ChannelSendResult> SendAsync(NotificationResult notification, CancellationToken cancellationToken)
    {
        await hub.Clients.Group(NotificationIdentity.Group(notification.OrganizationId, notification.RecipientUserId))
            .SendAsync("NotificationAvailable", new NotificationHint(notification.Id, notification.CreatedAt), cancellationToken);
        return new(true, false, "signalr:" + notification.Id.ToString("N"), null);
    }
}
