using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Monolith.Subscription.Application.Internal.OutboundServices;

namespace Monolith.Subscription.Infrastructure.Payments.Sandbox;

/// <summary>
///     Payment gateway that charges nothing: it creates checkouts with a local reference and accepts notifications in
///     its own JSON format, signed with HMAC-SHA256 like the webhooks of real providers (header
///     <c>X-Payment-Signature: sha256=&lt;hex&gt;</c>). It lets the whole flow of US-07 run and be tested end to end;
///     replacing it by a real provider means a new <see cref="IPaymentGateway" /> adapter, with no change in the
///     domain or the application layer.
/// </summary>
public sealed class SandboxPaymentGateway : IPaymentGateway, ISimulatedPaymentNotifier
{
    public const string SignatureHeader = "X-Payment-Signature";
    private const string SignaturePrefix = "sha256=";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
    };

    private readonly byte[] _secret;
    private readonly PaymentGatewayOptions _options;

    public SandboxPaymentGateway(IOptions<PaymentGatewayOptions> options)
    {
        _options = options.Value;
        _secret = string.IsNullOrWhiteSpace(_options.WebhookSecret)
            ? RandomNumberGenerator.GetBytes(32)
            : Encoding.UTF8.GetBytes(_options.WebhookSecret);
    }

    public string Provider => PaymentGatewayOptions.SandboxProvider;

    public bool Enabled => _options.AllowSimulatedPayments;

    public Task<PaymentCheckout> CreateCheckoutAsync(PaymentCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var reference = $"sbx_{Guid.NewGuid():N}";
        var checkoutUrl = string.IsNullOrWhiteSpace(_options.CheckoutBaseUrl)
            ? null
            : $"{_options.CheckoutBaseUrl.TrimEnd('/')}/{reference}";
        return Task.FromResult(new PaymentCheckout(reference, checkoutUrl));
    }

    public Task<PaymentNotification?> ReadNotificationAsync(PaymentNotificationMessage message,
        CancellationToken cancellationToken) =>
        Task.FromResult(ReadNotification(message.Payload, message.Header(SignatureHeader)));

    private PaymentNotification? ReadNotification(string payload, string? signature)
    {
        if (string.IsNullOrEmpty(payload) || !IsValidSignature(payload, signature)) return null;

        SandboxNotification? body;
        try
        {
            body = JsonSerializer.Deserialize<SandboxNotification>(payload, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (body is null || string.IsNullOrWhiteSpace(body.EventId) || string.IsNullOrWhiteSpace(body.Reference) ||
            string.IsNullOrWhiteSpace(body.Currency) || body.AmountInCents < 0 || !Enum.IsDefined(body.Status))
            return null;
        return new PaymentNotification(body.EventId, body.Reference, body.Status, body.AmountInCents, body.Currency,
            body.Reason);
    }

    public SignedPaymentNotification Create(string paymentReference, bool approved, long amountInCents,
        string currency)
    {
        var payload = JsonSerializer.Serialize(new SandboxNotification(
            $"evt_{Guid.NewGuid():N}", paymentReference,
            approved ? PaymentOutcome.Approved : PaymentOutcome.Rejected, amountInCents, currency,
            approved ? null : "Declined in the sandbox."), Json);
        return new SignedPaymentNotification(payload,
            new Dictionary<string, string> { [SignatureHeader] = Sign(payload) });
    }

    /// <summary>Signature the gateway puts in <see cref="SignatureHeader" /> for a body.</summary>
    public string Sign(string payload) =>
        SignaturePrefix + Convert.ToHexStringLower(HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(payload)));

    private bool IsValidSignature(string payload, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature)) return false;
        var expected = Encoding.ASCII.GetBytes(Sign(payload));
        var received = Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    private sealed record SandboxNotification(
        string EventId,
        string Reference,
        PaymentOutcome Status,
        long AmountInCents,
        string Currency,
        string? Reason);
}
