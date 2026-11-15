namespace Monolith.Subscription.Domain.Model;

/// <summary>
///     Failure cases of the Subscription bounded context (plan catalog, the plan each transport company has contracted
///     and its plan changes paid through the payment gateway). The interface layer maps them to HTTP status codes; the
///     name travels as the stable <c>code</c> of the problem details.
/// </summary>
public enum SubscriptionError
{
    None,

    // ── Plans ────────────────────────────────────────────────────────────────
    PlanNotFound,
    PlanNotAvailable,
    PlanAlreadyContracted,

    // ── Plan changes ─────────────────────────────────────────────────────────
    PlanChangeNotFound,
    PlanChangeAlreadyPending,
    PlanChangeNotPending,

    // ── Payments ─────────────────────────────────────────────────────────────
    PaymentNotificationInvalid,
    PaymentGatewayUnavailable,
    SimulatedPaymentsDisabled,

    // ── Technical ────────────────────────────────────────────────────────────
    InvalidSubscriptionData,
    ConcurrencyConflict,
    OperationCancelled,
    InternalServerError
}
