using Monolith.Subscription.Domain.Model.ValueObjects;

namespace Monolith.Subscription.Domain.Model.Entities;

/// <summary>
///     A request of the administrator to move the organization to another plan, with the price fixed when it was
///     requested and the reference the payment gateway gave to its checkout. It is also the history of the contract
///     (who asked for what, when it was paid, rejected or cancelled).
/// </summary>
public class PlanChange
{
    public const int ReferenceMaxLength = 120;
    public const int CheckoutUrlMaxLength = 500;
    public const int DetailMaxLength = 300;

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected PlanChange()
    {
        TargetPlanCode = new PlanCode();
        PreviousPlanCode = new PlanCode();
        Price = new Money();
        Status = new PlanChangeStatus();
    }

    internal PlanChange(PlanCode previousPlanCode, PlanCode targetPlanCode, Money price, Guid requestedBy,
        DateTimeOffset requestedAt)
    {
        if (requestedBy == Guid.Empty)
            throw new ArgumentException("The administrator who requests the change is required.", nameof(requestedBy));
        Id = Guid.NewGuid();
        PreviousPlanCode = previousPlanCode;
        TargetPlanCode = targetPlanCode;
        Price = price;
        RequestedBy = requestedBy;
        RequestedAt = requestedAt;
        Status = new PlanChangeStatus(PlanChangeStatus.PendingPayment);
    }

    public Guid Id { get; private set; }

    public PlanCode PreviousPlanCode { get; private set; }

    public PlanCode TargetPlanCode { get; private set; }

    /// <summary>Monthly price of the target plan when the change was requested; the payment must match it.</summary>
    public Money Price { get; private set; }

    public PlanChangeStatus Status { get; private set; }

    /// <summary>Identifier of the checkout in the payment gateway (null until it is created or for free plans).</summary>
    public string? PaymentReference { get; private set; }

    /// <summary>Where the administrator completes the payment (given by the gateway).</summary>
    public string? CheckoutUrl { get; private set; }

    /// <summary>Iam user id of the administrator who requested it.</summary>
    public Guid RequestedBy { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>Why it ended as it did (rejection reason of the gateway, amount mismatch, cancelled by the user…).</summary>
    public string? ResolutionDetail { get; private set; }

    internal void AttachCheckout(string reference, string? checkoutUrl)
    {
        var trimmed = (reference ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > ReferenceMaxLength)
            throw new ArgumentException($"The payment reference must have 1 to {ReferenceMaxLength} characters.",
                nameof(reference));
        if (checkoutUrl is { Length: > CheckoutUrlMaxLength })
            throw new ArgumentException($"The checkout URL cannot exceed {CheckoutUrlMaxLength} characters.",
                nameof(checkoutUrl));
        PaymentReference = trimmed;
        CheckoutUrl = checkoutUrl;
    }

    internal void Resolve(string status, DateTimeOffset at, string? detail)
    {
        Status = new PlanChangeStatus(status);
        ResolvedAt = at;
        ResolutionDetail = detail is null ? null : detail.Length <= DetailMaxLength ? detail : detail[..DetailMaxLength];
    }
}
