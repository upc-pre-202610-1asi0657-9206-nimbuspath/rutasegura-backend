using Monolith.Subscription.Domain.Model.Aggregates;
using Monolith.Subscription.Domain.Model.Entities;

namespace Monolith.Subscription.Application.QueryServices;

/// <summary>Limits in force for an organization: what Route Planning (and later Roster) enforce.</summary>
public record EffectivePlanLimits(string PlanCode, int MaxActiveRoutes, int MaxVehicles);

/// <summary>Contract of an organization as the administrator sees it.</summary>
/// <param name="ContractedPlan">Plan the organization contracted (may differ from the effective one when unpaid).</param>
/// <param name="EffectivePlan">Plan whose limits apply now.</param>
public record SubscriptionView(
    Guid OrganizationId,
    Plan ContractedPlan,
    Plan EffectivePlan,
    DateTimeOffset? StartedAt,
    DateTimeOffset? PaidUntil,
    PlanChange? PendingChange);
