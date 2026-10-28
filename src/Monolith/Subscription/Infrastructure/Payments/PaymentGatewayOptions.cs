namespace Monolith.Subscription.Infrastructure.Payments;

/// <summary>Payment gateway of the Subscription context (section <c>Subscription:PaymentGateway</c>).</summary>
public sealed class PaymentGatewayOptions
{
    public const string Section = "Subscription:PaymentGateway";
    public const string SandboxProvider = "Sandbox";

    /// <summary>Adapter behind <c>IPaymentGateway</c>. Only <c>Sandbox</c> exists until the team picks a provider.</summary>
    public string Provider { get; set; } = SandboxProvider;

    /// <summary>
    ///     Secret that signs the notifications (HMAC-SHA256). Never versioned: set it through the environment
    ///     (<c>Subscription__PaymentGateway__WebhookSecret</c>). When empty, the sandbox generates a random one per
    ///     process, so only the notifications it creates itself are accepted.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    ///     Lets an administrator approve or decline a sandbox checkout through the API. Only for development and
    ///     demos: with it enabled anyone with an administrator account can obtain a paid plan without paying.
    /// </summary>
    public bool AllowSimulatedPayments { get; set; }

    /// <summary>Optional page where the frontend shows the checkout; the reference is appended to it.</summary>
    public string? CheckoutBaseUrl { get; set; }
}
