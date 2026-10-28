namespace Monolith.Subscription.Domain.Model.ValueObjects;

/// <summary>
///     Lifecycle of a plan change (ADR-05.4). PENDING_PAYMENT: waiting for the gateway; the limits in force do not
///     change. APPLIED: the payment was verified (or the plan is free) and the new limits apply. REJECTED: the gateway
///     declined the payment. CANCELLED: the administrator withdrew it or the checkout could not be started.
///     Only a PENDING_PAYMENT change can still move; the other three are final.
/// </summary>
public record PlanChangeStatus
{
    public const string PendingPayment = "PENDING_PAYMENT";
    public const string Applied = "APPLIED";
    public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED";

    private static readonly string[] AllowedValues = [PendingPayment, Applied, Rejected, Cancelled];

    public PlanChangeStatus() : this(PendingPayment)
    {
    }

    public PlanChangeStatus(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (!AllowedValues.Contains(normalized))
            throw new ArgumentException($"'{value}' is not a valid plan change status.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; }

    public bool IsPending => Value == PendingPayment;

    public bool IsFinal => !IsPending;

    public override string ToString() => Value;
}
