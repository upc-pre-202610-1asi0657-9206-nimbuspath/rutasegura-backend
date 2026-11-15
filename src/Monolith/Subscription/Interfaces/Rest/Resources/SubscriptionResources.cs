namespace Monolith.Subscription.Interfaces.Rest.Resources;

/// <summary>A plan of the catalog with its monthly price (in soles) and limits.</summary>
public record PlanResource(
    string PlanId,
    string Name,
    string Description,
    decimal MonthlyPrice,
    string Currency,
    int MaxActiveRoutes,
    int MaxVehicles,
    bool IsDefault);

/// <summary>Contract of the organization.</summary>
/// <param name="ContractedPlan">Plan the organization contracted.</param>
/// <param name="EffectivePlan">Plan whose limits apply now (the default one when the paid period ended).</param>
public record SubscriptionResource(
    Guid OrganizationId,
    PlanResource ContractedPlan,
    PlanResource EffectivePlan,
    DateTimeOffset? StartedAt,
    DateTimeOffset? PaidUntil,
    PlanChangeResource? PendingChange);

/// <summary>A plan change and the state of its payment (report, contract 4).</summary>
public record PlanChangeResource(
    Guid PlanChangeId,
    string Status,
    string TargetPlanId,
    string PreviousPlanId,
    decimal Amount,
    string Currency,
    string? PaymentReference,
    string? CheckoutUrl,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ResolvedAt,
    string? Detail);

public record RequestPlanChangeResource(string TargetPlanId);

/// <summary>Sandbox only: approve or decline a simulated checkout.</summary>
public record CompleteSimulatedCheckoutResource(bool Approved);

/// <summary>Answer to the payment gateway (and to the sandbox checkout).</summary>
public record PaymentNotificationReceiptResource(
    string EventId,
    string Resolution,
    Guid? PlanChangeId,
    string? PlanChangeStatus);
