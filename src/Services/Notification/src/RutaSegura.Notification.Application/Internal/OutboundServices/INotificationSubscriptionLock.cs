namespace RutaSegura.Notification.Application.Internal.OutboundServices;

/// <summary>Exclusion de mutaciones por usuario/organizacion, incluida la confirmacion del Dispatcher.</summary>
public interface INotificationSubscriptionLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(Guid organization, Guid user, CancellationToken ct);
}
