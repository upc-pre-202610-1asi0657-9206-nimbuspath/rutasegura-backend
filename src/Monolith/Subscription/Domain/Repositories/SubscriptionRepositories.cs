using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Subscription.Domain.Model.Aggregates;
using Monolith.Subscription.Domain.Model.ValueObjects;

namespace Monolith.Subscription.Domain.Repositories;

/// <summary>The plan catalog.</summary>
public interface IPlanRepository
{
    Task<Plan?> FindByCodeAsync(PlanCode code, CancellationToken cancellationToken);

    /// <summary>The free plan of organizations without a paid contract.</summary>
    Task<Plan> GetDefaultAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Plan>> ListAvailableAsync(CancellationToken cancellationToken);
}

/// <summary>Contracts of the transport companies (with their plan changes).</summary>
public interface ITenantSubscriptionRepository
{
    Task<TenantSubscription?> FindByOrganizationAsync(OrganizationId organizationId,
        CancellationToken cancellationToken);

    /// <summary>The contract that has a plan change with this checkout reference of the gateway.</summary>
    Task<TenantSubscription?> FindByPaymentReferenceAsync(string paymentReference,
        CancellationToken cancellationToken);

    Task AddAsync(TenantSubscription subscription, CancellationToken cancellationToken);

    void Update(TenantSubscription subscription);
}

/// <summary>Notifications of the payment gateway that were already processed.</summary>
public interface IProcessedPaymentEventRepository
{
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken);

    Task AddAsync(ProcessedPaymentEvent paymentEvent, CancellationToken cancellationToken);
}
