using Monolith.Administration.Application.Internal.OutboundServices;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Subscription.Interfaces.ACL;

namespace Monolith.Administration.Infrastructure.Subscription;

/// <summary>
///     Adapter of <see cref="IPlanLimitsProvider" /> over the published facade of the Subscription context
///     (Customer/Supplier, ADR-05.4): Route Planning gets the limits of the plan in force without reading the
///     subscription tables. It replaces the former adapter that read the plan from configuration.
/// </summary>
public class SubscriptionPlanLimitsProvider(ISubscriptionContextFacade subscription) : IPlanLimitsProvider
{
    public async Task<PlanLimits> GetCurrentLimitsAsync(OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        var limits = await subscription.GetCurrentLimitsAsync(organizationId.Identifier, cancellationToken);
        return new PlanLimits(limits.PlanCode, limits.MaxActiveRoutes);
    }
}
