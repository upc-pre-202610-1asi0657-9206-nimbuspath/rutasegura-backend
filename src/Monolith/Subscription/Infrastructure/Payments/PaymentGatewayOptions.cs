namespace Monolith.Subscription.Infrastructure.Payments;

/// <summary>Payment gateway of the Subscription context (section <c>Subscription:PaymentGateway</c>).</summary>
public sealed class PaymentGatewayOptions
{
    public const string Section = "Subscription:PaymentGateway";
    public const string SandboxProvider = "Sandbox";
    public const string MercadoPagoProvider = "MercadoPago";

    /// <summary>
    ///     Adapter behind <c>IPaymentGateway</c>: <c>MercadoPago</c> (Checkout Pro) or <c>Sandbox</c> (charges nothing;
    ///     development, demos and tests).
    /// </summary>
    public string Provider { get; set; } = SandboxProvider;

    /// <summary>
    ///     Secret that signs the notifications (HMAC-SHA256). Never versioned: set it through the environment
    ///     (<c>Subscription__PaymentGateway__WebhookSecret</c>). With Mercado Pago it is the secret key of the webhook
    ///     (Your integrations, Webhooks) and is required. When empty, the sandbox generates a random one per process, so
    ///     only the notifications it creates itself are accepted.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    ///     Lets an administrator approve or decline a sandbox checkout through the API. Only for development and
    ///     demos: with it enabled anyone with an administrator account can obtain a paid plan without paying.
    /// </summary>
    public bool AllowSimulatedPayments { get; set; }

    /// <summary>Sandbox only: optional page where the frontend shows the checkout; the reference is appended to it.</summary>
    public string? CheckoutBaseUrl { get; set; }

    public MercadoPagoOptions MercadoPago { get; set; } = new();
}

/// <summary>Mercado Pago Checkout Pro (section <c>Subscription:PaymentGateway:MercadoPago</c>).</summary>
public sealed class MercadoPagoOptions
{
    /// <summary>
    ///     Access Token of the application (the TEST credentials outside production). Never versioned: set it through
    ///     <c>Subscription__PaymentGateway__MercadoPago__AccessToken</c> or user-secrets.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    ///     Public HTTPS address of <c>POST /api/v1/subscription/payments/notifications</c> (through the API Gateway).
    ///     Mercado Pago cannot reach <c>localhost</c>: use the deployed URL or a tunnel.
    /// </summary>
    public string NotificationUrl { get; set; } = string.Empty;

    /// <summary>Optional frontend page where Mercado Pago sends the administrator back after paying.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Text on the card statement of the buyer.</summary>
    public string StatementDescriptor { get; set; } = "RUTASEGURA";

    public string ApiBaseUrl { get; set; } = "https://api.mercadopago.com/";

    public int TimeoutSeconds { get; set; } = 10;
}
