using Monolith.Subscription.Application.QueryServices;
using Monolith.Subscription.Domain.Model.Queries;

namespace Monolith.Subscription.Interfaces.ACL;

public class SubscriptionContextFacade(ISubscriptionQueryService queryService) : ISubscriptionContextFacade
{
    public async Task<SubscriptionLimits> GetCurrentLimitsAsync(Guid organizationId,
        CancellationToken cancellationToken)
    {
        var limits = await queryService.Handle(new GetEffectiveLimitsQuery(organizationId), cancellationToken);
        return new SubscriptionLimits(limits.PlanCode, limits.MaxActiveRoutes, limits.MaxVehicles);
    }
}
