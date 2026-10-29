namespace Monolith.Subscription.Domain.Model.Commands;

/// <summary>The administrator asks to move the organization to another plan (US-07).</summary>
public record RequestPlanChangeCommand(Guid OrganizationId, Guid RequestedBy, string TargetPlanCode);

/// <summary>The administrator withdraws a change that is still waiting for its payment.</summary>
public record CancelPlanChangeCommand(Guid OrganizationId, Guid PlanChangeId);

/// <summary>
///     A notification of the payment gateway, exactly as it arrived: the raw body, its headers and its query string.
///     The gateway adapter verifies the signature before anything is trusted.
/// </summary>
public record ProcessPaymentNotificationCommand(
    string Payload,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string>? Query = null);

/// <summary>
///     Sandbox gateway only: the administrator finishes a simulated checkout (approve or decline) so the whole flow
///     can be demonstrated without a real payment provider.
/// </summary>
public record CompleteSimulatedCheckoutCommand(Guid OrganizationId, string PaymentReference, bool Approved);
