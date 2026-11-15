using System.Security.Cryptography;
using System.Text;
using RutaSegura.Notification.Application.Internal.OutboundServices;

namespace RutaSegura.Notification.Infrastructure.Channels;

public sealed class NotificationSubscriptionLock(MySqlDispatchLock locks) : INotificationSubscriptionLock
{
    public Task<IAsyncDisposable?> TryAcquireAsync(Guid organization, Guid user, CancellationToken ct)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"subscription:{organization:N}:{user:N}"));
        return locks.TryAcquireAsync(new Guid(hash.AsSpan(0, 16)), ct);
    }
}
