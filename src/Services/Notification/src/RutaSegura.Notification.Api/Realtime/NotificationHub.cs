using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RutaSegura.Notification.Api.Security;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;
using Microsoft.AspNetCore.Authentication;

namespace RutaSegura.Notification.Api.Realtime;

public sealed record NotificationHint(Guid NotificationId, DateTimeOffset CreatedAt);

[Authorize(Policy = "notification-user")]
public sealed class NotificationHub(NotificationRealtimeSessions sessions) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Context.User is null || !NotificationIdentity.Valid(Context.User)) { Context.Abort(); return; }
        var http = Context.GetHttpContext();
        var token = http is null ? null : await http.GetTokenAsync("access_token");
        if (!sessions.Register(Context, token ?? "")) { Context.Abort(); return; }
        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId,
                NotificationIdentity.Group(NotificationIdentity.OrganizationId(Context.User), NotificationIdentity.UserId(Context.User)), Context.ConnectionAborted);
            await base.OnConnectedAsync();
        }
        catch
        {
            sessions.Remove(Context.ConnectionId);
            throw;
        }
    }
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        sessions.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
    // No existen metodos JoinGroup/Subscribe con ids elegidos por el cliente.
}

public sealed class SignalRNotificationSender(IHubContext<NotificationHub> hub, NotificationRealtimeSessions sessions) : IRealtimeNotificationSender
{
    public async Task<ChannelSendResult> SendAsync(NotificationResult notification, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        await sessions.ValidateRecipientAsync(notification.OrganizationId, notification.RecipientUserId, timeout.Token);
        await hub.Clients.Group(NotificationIdentity.Group(notification.OrganizationId, notification.RecipientUserId))
            .SendAsync("NotificationAvailable", new NotificationHint(notification.Id, notification.CreatedAt), timeout.Token);
        return new(true, false, "signalr:" + notification.Id.ToString("N"), null);
    }
}
