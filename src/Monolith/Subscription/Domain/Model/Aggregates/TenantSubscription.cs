using Monolith.Shared.Domain.Model.Entities;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Subscription.Domain.Model.Entities;
using Monolith.Subscription.Domain.Model.Exceptions;
using Monolith.Subscription.Domain.Model.ValueObjects;

namespace Monolith.Subscription.Domain.Model.Aggregates;

/// <summary>How a payment notification of the gateway ended for its plan change.</summary>
public enum PaymentResolution
{
    /// <summary>The payment was verified: the new plan and its limits apply from now on.</summary>
    Applied,

    /// <summary>The same confirmation arrived again; nothing changes (idempotent).</summary>
    AlreadyApplied,

    /// <summary>The gateway declined the payment; the limits in force do not change.</summary>
    Rejected,

    /// <summary>The change was already rejected or cancelled; a late notification does not activate it.</summary>
    AlreadyResolved,

    /// <summary>The amount or currency paid is not the price of the plan; the change is rejected.</summary>
    AmountMismatch
}

/// <summary>
///     The contract of one transport company (tenant): the plan it has, until when it is paid and its plan changes.
///     It is the source of truth Route Planning asks (through the facade of this context) before activating a route.
/// </summary>
/// <remarks>
///     ADR-05.4: a paid change goes through PENDING_PAYMENT and its limits apply only after the payment is verified;
///     repeated or late notifications never apply a change twice; at most one change is pending at a time.
///     <see cref="Version" /> is an optimistic concurrency token: two notifications processed at the same time cannot
///     both change the contract.
/// </remarks>
public class TenantSubscription : IAuditableEntity
{
    /// <summary>A paid plan lasts one month from the verified payment.</summary>
    public static readonly int PaidPeriodMonths = 1;

    private readonly List<PlanChange> _planChanges = [];

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected TenantSubscription()
    {
        OrganizationId = new OrganizationId(Guid.Empty);
        PlanCode = new PlanCode();
    }

    /// <summary>Every organization starts on the free default plan.</summary>
    public TenantSubscription(OrganizationId organizationId, Plan defaultPlan, DateTimeOffset now)
    {
        if (organizationId is null || organizationId.Identifier == Guid.Empty)
            throw new ArgumentException("The organization is required.", nameof(organizationId));
        ArgumentNullException.ThrowIfNull(defaultPlan);
        if (!defaultPlan.IsDefault)
            throw new ArgumentException("A new subscription starts on the default plan.", nameof(defaultPlan));
        OrganizationId = organizationId;
        PlanCode = defaultPlan.Code;
        StartedAt = now;
    }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>Plan contracted by the organization.</summary>
    public PlanCode PlanCode { get; private set; }

    /// <summary>When the current plan started (the organization joined, or its last change was applied).</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>End of the paid period of a paid plan; null for the free default plan.</summary>
    public DateTimeOffset? PaidUntil { get; private set; }

    public IReadOnlyList<PlanChange> PlanChanges => _planChanges;

    public PlanChange? PendingChange => _planChanges.SingleOrDefault(change => change.Status.IsPending);

    /// <summary>Optimistic concurrency token, incremented by every change of the contract.</summary>
    public int Version { get; private set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    ///     The plan whose limits apply now: the contracted one while it is paid, otherwise the default plan (an
    ///     unpaid period never grants the limits of a paid plan).
    /// </summary>
    public PlanCode EffectivePlanCode(PlanCode defaultPlanCode, DateTimeOffset now) =>
        PaidUntil is { } until && now >= until ? defaultPlanCode : PlanCode;

    /// <summary>
    ///     Starts the move to <paramref name="target" />. A free plan applies at once; a paid one waits for its payment.
    /// </summary>
    public PlanChange RequestPlanChange(Plan target, PlanCode defaultPlanCode, Guid requestedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.IsAvailable)
            throw new SubscriptionDomainException(SubscriptionError.PlanNotAvailable,
                $"The plan {target.Code} is no longer offered.");
        if (PendingChange is { } pending)
            throw new SubscriptionDomainException(SubscriptionError.PlanChangeAlreadyPending,
                $"The change to {pending.TargetPlanCode} is waiting for its payment; complete or cancel it first.");
        var current = EffectivePlanCode(defaultPlanCode, now);
        if (target.Code == current)
            throw new SubscriptionDomainException(SubscriptionError.PlanAlreadyContracted,
                $"The organization already has the plan {current}.");

        var change = new PlanChange(current, target.Code, target.MonthlyPrice, requestedBy, now);
        _planChanges.Add(change);
        if (!target.RequiresPayment) Apply(change, now, paidUntil: null, "Free plan: applied without payment.");
        Version++;
        return change;
    }

    /// <summary>Records the checkout the payment gateway created for a pending change.</summary>
    public void AttachCheckout(Guid planChangeId, string paymentReference, string? checkoutUrl)
    {
        var change = RequirePending(planChangeId);
        if (change.PaymentReference is not null)
            throw new SubscriptionDomainException(SubscriptionError.PlanChangeNotPending,
                "The plan change already has a checkout.");
        change.AttachCheckout(paymentReference, checkoutUrl);
        Version++;
    }

    /// <summary>The administrator withdraws a pending change, or its checkout could not be started.</summary>
    public PlanChange CancelPlanChange(Guid planChangeId, string detail, DateTimeOffset now)
    {
        var change = RequirePending(planChangeId);
        change.Resolve(PlanChangeStatus.Cancelled, now, detail);
        Version++;
        return change;
    }

    /// <summary>The gateway confirmed the payment of the checkout <paramref name="paymentReference" />.</summary>
    public PaymentResolution ConfirmPayment(string paymentReference, Money paid, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(paid);
        var change = RequireByReference(paymentReference);
        if (change.Status.Value == PlanChangeStatus.Applied) return PaymentResolution.AlreadyApplied;
        if (change.Status.IsFinal) return PaymentResolution.AlreadyResolved;

        Version++;
        if (paid != change.Price)
        {
            change.Resolve(PlanChangeStatus.Rejected, now,
                $"Amount mismatch: paid {paid}, the plan costs {change.Price}.");
            return PaymentResolution.AmountMismatch;
        }

        Apply(change, now, now.AddMonths(PaidPeriodMonths), "Payment verified.");
        return PaymentResolution.Applied;
    }

    /// <summary>The gateway declined the payment of the checkout <paramref name="paymentReference" />.</summary>
    public PaymentResolution RejectPayment(string paymentReference, string? reason, DateTimeOffset now)
    {
        var change = RequireByReference(paymentReference);
        if (change.Status.IsFinal) return PaymentResolution.AlreadyResolved;
        change.Resolve(PlanChangeStatus.Rejected, now, reason ?? "Payment declined by the gateway.");
        Version++;
        return PaymentResolution.Rejected;
    }

    public PlanChange? FindPlanChange(Guid planChangeId) =>
        _planChanges.SingleOrDefault(change => change.Id == planChangeId);

    private void Apply(PlanChange change, DateTimeOffset now, DateTimeOffset? paidUntil, string detail)
    {
        change.Resolve(PlanChangeStatus.Applied, now, detail);
        PlanCode = change.TargetPlanCode;
        StartedAt = now;
        PaidUntil = paidUntil;
    }

    private PlanChange RequirePending(Guid planChangeId)
    {
        var change = FindPlanChange(planChangeId) ??
                     throw new SubscriptionDomainException(SubscriptionError.PlanChangeNotFound,
                         "The plan change does not exist.");
        if (!change.Status.IsPending)
            throw new SubscriptionDomainException(SubscriptionError.PlanChangeNotPending,
                $"The plan change is already {change.Status}.");
        return change;
    }

    private PlanChange RequireByReference(string paymentReference) =>
        _planChanges.SingleOrDefault(change => change.PaymentReference == paymentReference) ??
        throw new SubscriptionDomainException(SubscriptionError.PlanChangeNotFound,
            "No plan change of this organization has that payment reference.");
}
