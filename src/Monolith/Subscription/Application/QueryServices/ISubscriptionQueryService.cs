using Monolith.Subscription.Domain.Model.Aggregates;
using Monolith.Subscription.Domain.Model.Entities;
using Monolith.Subscription.Domain.Model.Queries;

namespace Monolith.Subscription.Application.QueryServices;

public interface ISubscriptionQueryService
{
    Task<IReadOnlyList<Plan>> Handle(GetAvailablePlansQuery query, CancellationToken cancellationToken);

    /// <summary>Organizations that never contracted a plan are shown on the default plan.</summary>
    Task<SubscriptionView> Handle(GetSubscriptionQuery query, CancellationToken cancellationToken);

    Task<PlanChange?> Handle(GetPlanChangeQuery query, CancellationToken cancellationToken);

    Task<EffectivePlanLimits> Handle(GetEffectiveLimitsQuery query, CancellationToken cancellationToken);
}
