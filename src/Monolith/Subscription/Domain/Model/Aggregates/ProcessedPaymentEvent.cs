namespace Monolith.Subscription.Domain.Model.Aggregates;

/// <summary>
///     A notification of the payment gateway that was already handled (Idempotent Consumer, PT-04). Gateways retry
///     their notifications, so the event id is unique: a repeated one is acknowledged without touching the contract.
///     It also keeps the trace of every payment the platform received (NFR-07).
/// </summary>
public class ProcessedPaymentEvent
{
    public const int EventIdMaxLength = 120;
    public const int ReferenceMaxLength = 120;
    public const int OutcomeMaxLength = 20;
    public const int ResolutionMaxLength = 30;

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected ProcessedPaymentEvent()
    {
        EventId = string.Empty;
        PaymentReference = string.Empty;
        Outcome = string.Empty;
        Resolution = string.Empty;
    }

    public ProcessedPaymentEvent(string eventId, string paymentReference, string outcome, string resolution,
        Guid? organizationId, DateTimeOffset receivedAt)
    {
        EventId = Required(eventId, EventIdMaxLength, nameof(eventId));
        PaymentReference = Required(paymentReference, ReferenceMaxLength, nameof(paymentReference));
        Outcome = Required(outcome, OutcomeMaxLength, nameof(outcome));
        Resolution = Required(resolution, ResolutionMaxLength, nameof(resolution));
        OrganizationId = organizationId;
        ReceivedAt = receivedAt;
    }

    /// <summary>Id the gateway gives to the notification (unique: a repeated one is a retry).</summary>
    public string EventId { get; private set; }

    public string PaymentReference { get; private set; }

    /// <summary>What the gateway said: APPROVED or REJECTED.</summary>
    public string Outcome { get; private set; }

    /// <summary>What RutaSegura did with it (a <see cref="PaymentResolution" /> name or UNMATCHED).</summary>
    public string Resolution { get; private set; }

    /// <summary>Organization of the matching plan change; null when no change has that reference.</summary>
    public Guid? OrganizationId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    private static string Required(string value, int maxLength, string name)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length is 0 || trimmed.Length > maxLength)
            throw new ArgumentException($"The value must have 1 to {maxLength} characters.", name);
        return trimmed;
    }
}
