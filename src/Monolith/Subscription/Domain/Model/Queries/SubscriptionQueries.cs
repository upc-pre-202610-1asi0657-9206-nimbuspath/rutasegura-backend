namespace Monolith.Subscription.Domain.Model.Queries;

/// <summary>Plans that can be contracted (public price list).</summary>
public record GetAvailablePlansQuery;

/// <summary>Contract of the organization: plan in force, its limits and the pending change, if any.</summary>
public record GetSubscriptionQuery(Guid OrganizationId);

/// <summary>A plan change of the organization (to follow its payment).</summary>
public record GetPlanChangeQuery(Guid OrganizationId, Guid PlanChangeId);

/// <summary>Limits Route Planning enforces (asked through the facade of this context).</summary>
public record GetEffectiveLimitsQuery(Guid OrganizationId);
