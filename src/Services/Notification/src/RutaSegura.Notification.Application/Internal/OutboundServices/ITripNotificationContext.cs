namespace RutaSegura.Notification.Application.Internal.OutboundServices;

/// <summary>Estado autorizado de Trip & Tracking; no calcula geocercas, ETA ni almacena GPS.</summary>
public interface ITripNotificationContext
{
    Task<Guid?> DriverUserIdAsync(Guid organizationId, Guid tripId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
    Task<bool> IsActiveAsync(Guid organizationId, Guid tripId, CancellationToken cancellationToken);
}
