namespace Monolith.Subscription.Application.Internal.OutboundServices;

/// <summary>
///     Port towards the payment gateway (DP-11 Adapter / Anti-Corruption Layer). The domain never sees the format of
///     the provider: an adapter translates its checkouts and notifications to these records. The infrastructure ships
///     the Mercado Pago adapter (the provider chosen by the team) and a sandbox one for development and tests.
/// </summary>
/// <remarks>ADR-05.4: these calls are made outside database transactions.</remarks>
public interface IPaymentGateway
{
    /// <summary>Name of the provider behind the adapter (e.g. <c>Sandbox</c>, <c>MercadoPago</c>).</summary>
    string Provider { get; }

    /// <summary>Creates the checkout where the administrator pays the plan.</summary>
    /// <exception cref="PaymentGatewayUnavailableException">The gateway could not be reached or refused it.</exception>
    Task<PaymentCheckout> CreateCheckoutAsync(PaymentCheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>
    ///     Verifies the signature of a notification and translates it. Returns null when the signature is missing or
    ///     wrong, or the request is not a notification of this gateway: such a request must never change a contract.
    /// </summary>
    /// <exception cref="PaymentGatewayUnavailableException">
    ///     The notification is authentic but the gateway could not be asked for the payment it refers to.
    /// </exception>
    Task<PaymentNotification?> ReadNotificationAsync(PaymentNotificationMessage message,
        CancellationToken cancellationToken);
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

/// <summary>
///     A notification exactly as it reached the webhook: the raw body (signatures cover its exact bytes), the headers
///     and the query string. Each provider signs a different part of it.
/// </summary>
public record PaymentNotificationMessage(
    string Payload,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Query)
{
    /// <summary>Value of a header, whatever its case; null when it was not sent.</summary>
    public string? Header(string name) => Find(Headers, name);

    /// <summary>Value of a query string parameter, whatever its case; null when it was not sent.</summary>
    public string? QueryValue(string name) => Find(Query, name);

    private static string? Find(IReadOnlyDictionary<string, string> values, string name) =>
        values.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}

public enum PaymentOutcome
{
    Approved,
    Rejected,

    /// <summary>The payment is not final yet (e.g. in process): acknowledged, nothing changes.</summary>
    Pending
}

/// <summary>A verified notification of the gateway about one checkout.</summary>
public record PaymentNotification(
    string EventId,
    string PaymentReference,
    PaymentOutcome Outcome,
    long AmountInCents,
    string Currency,
    string? Reason);

/// <summary>The payment gateway could not be reached or refused the request (down, timeout, bad credentials).</summary>
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

/// <summary>A notification body and the headers that carry its signature.</summary>
public record SignedPaymentNotification(string Payload, IReadOnlyDictionary<string, string> Headers);
