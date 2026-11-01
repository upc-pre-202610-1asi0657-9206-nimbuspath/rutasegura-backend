using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Subscription.Application.QueryServices;
using Monolith.Subscription.Domain.Model.Aggregates;
using Monolith.Subscription.Domain.Model.Entities;
using Monolith.Subscription.Domain.Model.Queries;
using Monolith.Subscription.Domain.Repositories;

namespace Monolith.Subscription.Application.Internal.QueryServices;

public class SubscriptionQueryService(
    IPlanRepository planRepository,
    ITenantSubscriptionRepository subscriptionRepository,
    TimeProvider clock) : ISubscriptionQueryService
{
    public Task<IReadOnlyList<Plan>> Handle(GetAvailablePlansQuery query, CancellationToken cancellationToken) =>
        planRepository.ListAvailableAsync(cancellationToken);

    public async Task<SubscriptionView> Handle(GetSubscriptionQuery query, CancellationToken cancellationToken)
    {
        var defaultPlan = await planRepository.GetDefaultAsync(cancellationToken);
        var subscription =
            await subscriptionRepository.FindByOrganizationAsync(new OrganizationId(query.OrganizationId),
                cancellationToken);
        if (subscription is null)
            return new SubscriptionView(query.OrganizationId, defaultPlan, defaultPlan, null, null, null);

        var contracted = await planRepository.FindByCodeAsync(subscription.PlanCode, cancellationToken) ?? defaultPlan;
        var effectiveCode = subscription.EffectivePlanCode(defaultPlan.Code, clock.GetUtcNow());
        var effective = effectiveCode == contracted.Code ? contracted : defaultPlan;
        return new SubscriptionView(query.OrganizationId, contracted, effective, subscription.StartedAt,
            subscription.PaidUntil, subscription.PendingChange);
    }

    public async Task<PlanChange?> Handle(GetPlanChangeQuery query, CancellationToken cancellationToken)
    {
        var subscription =
            await subscriptionRepository.FindByOrganizationAsync(new OrganizationId(query.OrganizationId),
                cancellationToken);
        return subscription?.FindPlanChange(query.PlanChangeId);
    }

    public async Task<EffectivePlanLimits> Handle(GetEffectiveLimitsQuery query, CancellationToken cancellationToken)
    {
        var view = await Handle(new GetSubscriptionQuery(query.OrganizationId), cancellationToken);
        return new EffectivePlanLimits(view.EffectivePlan.Code.Value, view.EffectivePlan.MaxActiveRoutes,
            view.EffectivePlan.MaxVehicles);
    }
}
