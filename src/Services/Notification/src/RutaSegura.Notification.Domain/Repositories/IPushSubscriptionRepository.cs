using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Domain.Repositories;

public interface IPushSubscriptionRepository
{
    Task<PushSubscription?> GetAsync(Guid organizationId, Guid userId, Guid subscriptionId, CancellationToken cancellationToken);
    Task<PushSubscription?> FindByInstallationAsync(Guid organizationId, Guid userId, Guid installationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PushSubscription>> ListEnabledAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);
    void Add(PushSubscription subscription);
}
