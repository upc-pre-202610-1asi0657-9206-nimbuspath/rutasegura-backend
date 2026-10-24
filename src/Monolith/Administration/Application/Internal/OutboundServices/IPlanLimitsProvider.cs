using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Administration.Application.Internal.OutboundServices;

/// <summary>
///     Port towards the Subscription context (Customer/Supplier: "the active-route limit depends on the
///     contracted plan"). Route Planning never reads Subscription data directly.
/// </summary>
/// <remarks>
///     Subscription is not developed yet. Until it is, the infrastructure layer provides an adapter with a
///     configurable default plan; when Subscription exists the adapter calls its application facade instead,
///     with no change in this layer.
/// </remarks>
public interface IPlanLimitsProvider
{
    Task<PlanLimits> GetCurrentLimitsAsync(OrganizationId organizationId, CancellationToken cancellationToken);
}
