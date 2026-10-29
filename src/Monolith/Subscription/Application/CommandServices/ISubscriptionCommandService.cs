using Monolith.Shared.Application.Model;
using Monolith.Subscription.Domain.Model.Aggregates;
using Monolith.Subscription.Domain.Model.Commands;
using Monolith.Subscription.Domain.Model.Entities;

namespace Monolith.Subscription.Application.CommandServices;

/// <summary>Use cases that change a contract (US-07).</summary>
public interface ISubscriptionCommandService
{
    /// <summary>
    ///     Starts a plan change: a free plan applies at once; a paid one stays PENDING_PAYMENT with a checkout of the
    ///     payment gateway, and the limits in force do not change until the payment is verified.
    /// </summary>
    Task<Result<PlanChange>> Handle(RequestPlanChangeCommand command, CancellationToken cancellationToken);

    Task<Result<PlanChange>> Handle(CancelPlanChangeCommand command, CancellationToken cancellationToken);

    /// <summary>
    ///     Applies a notification of the gateway once: verified signature, idempotent by event id, amount checked
    ///     against the price; a late or repeated notification never applies a change twice.
    /// </summary>
    Task<Result<PaymentNotificationReceipt>> Handle(ProcessPaymentNotificationCommand command,
        CancellationToken cancellationToken);

    Task<Result<PaymentNotificationReceipt>> Handle(CompleteSimulatedCheckoutCommand command,
        CancellationToken cancellationToken);
}

/// <summary>What happened with a notification of the gateway.</summary>
/// <param name="Resolution">A <see cref="PaymentResolution" /> name, DUPLICATE (already processed) or UNMATCHED.</param>
public record PaymentNotificationReceipt(string EventId, string Resolution, Guid? PlanChangeId, string? PlanChangeStatus);
