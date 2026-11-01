using Monolith.Subscription.Application.CommandServices;
using Monolith.Subscription.Application.QueryServices;
using Monolith.Subscription.Domain.Model.Aggregates;
using Monolith.Subscription.Domain.Model.Entities;
using Monolith.Subscription.Interfaces.Rest.Resources;

namespace Monolith.Subscription.Interfaces.Rest.Transform;

public static class SubscriptionResourceAssembler
{
    public static PlanResource ToResource(Plan plan) =>
        new(plan.Code.Value, plan.Name, plan.Description, plan.MonthlyPrice.Amount, plan.MonthlyPrice.Currency,
            plan.MaxActiveRoutes, plan.MaxVehicles, plan.IsDefault);

    public static SubscriptionResource ToResource(SubscriptionView view) =>
        new(view.OrganizationId, ToResource(view.ContractedPlan), ToResource(view.EffectivePlan), view.StartedAt,
            view.PaidUntil, view.PendingChange is null ? null : ToResource(view.PendingChange));

    public static PlanChangeResource ToResource(PlanChange change) =>
        new(change.Id, change.Status.Value, change.TargetPlanCode.Value, change.PreviousPlanCode.Value,
            change.Price.Amount, change.Price.Currency, change.PaymentReference, change.CheckoutUrl,
            change.RequestedAt, change.ResolvedAt, change.ResolutionDetail);

    public static PaymentNotificationReceiptResource ToResource(PaymentNotificationReceipt receipt) =>
        new(receipt.EventId, receipt.Resolution, receipt.PlanChangeId, receipt.PlanChangeStatus);
}
