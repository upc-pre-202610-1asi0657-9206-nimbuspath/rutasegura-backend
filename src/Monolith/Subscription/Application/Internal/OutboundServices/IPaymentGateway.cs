namespace Monolith.Subscription.Application.Internal.OutboundServices;

/// <summary>
///     Port towards the payment gateway (DP-11 Adapter / Anti-Corruption Layer). The domain never sees the format of
///     the provider: an adapter translates its checkouts and notifications to these records. The report does not fix a
///     provider yet (the context diagram names Stripe as an example), so the infrastructure ships a sandbox adapter and
///     a real provider only needs a new implementation of this interface.
/// </summary>
/// <remarks>ADR-05.4: these calls are made outside database transactions.</remarks>
public interface IPaymentGateway
{
    /// <summary>Name of the provider behind the adapter (e.g. <c>Sandbox</c>).</summary>
    string Provider { get; }

    /// <summary>HTTP header where the provider sends the signature of its notifications.</summary>
    string SignatureHeader { get; }

    /// <summary>Creates the checkout where the administrator pays the plan.</summary>
    /// <exception cref="PaymentGatewayUnavailableException">The gateway could not be reached or refused it.</exception>
    Task<PaymentCheckout> CreateCheckoutAsync(PaymentCheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>
    ///     Verifies the signature of a notification and translates it. Returns null when the signature is missing or
    ///     wrong, or the body is not a notification of this gateway: such a request must never change a contract.
    /// </summary>
    PaymentNotification? ReadNotification(string payload, string? signature);
}

/// <summary>What the gateway needs to charge one month of a plan.</summary>
public record PaymentCheckoutRequest(
    Guid PlanChangeId,
    Guid OrganizationId,
    string PlanCode,
    string PlanName,
    long AmountInCents,
    string Currency);

/// <summary>The checkout created by the gateway: its reference and where the administrator pays.</summary>
public record PaymentCheckout(string Reference, string? CheckoutUrl);

public enum PaymentOutcome
{
    Approved,
    Rejected
}

/// <summary>A verified notification of the gateway about one checkout.</summary>
public record PaymentNotification(
    string EventId,
    string PaymentReference,
    PaymentOutcome Outcome,
    long AmountInCents,
    string Currency,
    string? Reason);

/// <summary>The payment gateway could not create the checkout (down, timeout, rejected the request).</summary>
public class PaymentGatewayUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
///     Sandbox gateway only: produces a signed notification exactly as the gateway would send it, so a simulated
///     checkout goes through the same verification and idempotency as a real one.
/// </summary>
public interface ISimulatedPaymentNotifier
{
    /// <summary>False outside environments where simulated payments are allowed (never in production).</summary>
    bool Enabled { get; }

    SignedPaymentNotification Create(string paymentReference, bool approved, long amountInCents, string currency);
}

public record SignedPaymentNotification(string Payload, string Signature);
