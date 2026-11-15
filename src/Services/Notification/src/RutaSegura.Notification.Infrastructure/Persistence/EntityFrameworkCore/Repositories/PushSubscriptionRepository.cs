using Microsoft.EntityFrameworkCore;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories;

public sealed class PushSubscriptionRepository(NotificationDbContext context) : IPushSubscriptionRepository
{
    public Task<PushSubscription?> GetAsync(Guid organizationId, Guid userId, Guid subscriptionId, CancellationToken cancellationToken) =>
        context.PushSubscriptions.SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.UserId == userId && s.Id == subscriptionId, cancellationToken);
    public Task<PushSubscription?> FindByInstallationAsync(Guid organizationId, Guid userId, Guid installationId, CancellationToken cancellationToken) =>
        context.PushSubscriptions.SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.UserId == userId && s.InstallationId == installationId, cancellationToken);
    public async Task<IReadOnlyList<PushSubscription>> ListEnabledAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        await context.PushSubscriptions.Where(s => s.OrganizationId == organizationId && s.UserId == userId && s.IsEnabled)
            .OrderBy(s => s.Id).ToArrayAsync(cancellationToken);
    public void Add(PushSubscription subscription) => context.PushSubscriptions.Add(subscription);
}
