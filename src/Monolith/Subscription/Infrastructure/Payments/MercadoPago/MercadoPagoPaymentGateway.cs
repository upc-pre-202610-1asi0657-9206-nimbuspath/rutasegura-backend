using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Monolith.Subscription.Application.Internal.OutboundServices;

namespace Monolith.Subscription.Infrastructure.Payments.MercadoPago;

/// <summary>
///     Mercado Pago Checkout Pro. A checkout is a payment preference whose <c>external_reference</c> is the reference
///     of the plan change; the administrator pays on the page of Mercado Pago (<c>init_point</c>).
/// </summary>
/// <remarks>
///     A webhook only says that a payment changed: the adapter verifies its <c>x-signature</c> (HMAC-SHA256 of
///     <c>id:{data.id};request-id:{x-request-id};ts:{ts};</c> with the secret key of the webhook) and then reads the
///     payment from the API, so the status and the amount always come from Mercado Pago and never from the request.
///     The event id is the payment plus its status: Mercado Pago sends several notifications per payment, and only the
///     first one with each status is applied.
/// </remarks>
public sealed class MercadoPagoPaymentGateway : IPaymentGateway
{
    public const string SignatureHeader = "x-signature";
    public const string RequestIdHeader = "x-request-id";
    public const string ReferencePrefix = "mp_";
    private const int MaxReferenceLength = 120;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly MercadoPagoOptions _options;
    private readonly byte[] _webhookSecret;

    public MercadoPagoPaymentGateway(HttpClient http, IOptions<PaymentGatewayOptions> options)
    {
        _http = http;
        _options = options.Value.MercadoPago;
        _webhookSecret = Encoding.UTF8.GetBytes(options.Value.WebhookSecret);
    }

    public string Provider => PaymentGatewayOptions.MercadoPagoProvider;

    public async Task<PaymentCheckout> CreateCheckoutAsync(PaymentCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var reference = $"{ReferencePrefix}{request.PlanChangeId:N}";
        var returnUrl = string.IsNullOrWhiteSpace(_options.ReturnUrl) ? null : _options.ReturnUrl;
        var preference = new PreferenceRequest(
            [
                new PreferenceItem(request.PlanCode, $"{request.PlanName} (1 mes)", 1,
                    request.AmountInCents / 100m, request.Currency)
            ],
            reference,
            NotificationUrl(),
            _options.StatementDescriptor,
            returnUrl is null ? null : new BackUrls(returnUrl, returnUrl, returnUrl),
            returnUrl is null ? null : "approved",
            new Dictionary<string, string>
            {
                ["organization_id"] = request.OrganizationId.ToString(),
                ["plan_change_id"] = request.PlanChangeId.ToString()
            });

        using var message = Authorized(HttpMethod.Post, "checkout/preferences");
        // A retried request with the same key does not create a second preference.
        message.Headers.Add("X-Idempotency-Key", request.PlanChangeId.ToString());
        message.Content = JsonContent.Create(preference, options: Json);

        var created = await SendAsync<PreferenceResponse>(message, "create the checkout", cancellationToken);
        if (created is null || string.IsNullOrWhiteSpace(created.InitPoint))
            throw new PaymentGatewayUnavailableException("Mercado Pago did not return the checkout page.");
        return new PaymentCheckout(reference, created.InitPoint);
    }

    public async Task<PaymentNotification?> ReadNotificationAsync(PaymentNotificationMessage message,
        CancellationToken cancellationToken)
    {
        var body = ReadBody(message.Payload);
        var type = message.QueryValue("type") ?? body?.Type;
        if (type is not null && !string.Equals(type, "payment", StringComparison.OrdinalIgnoreCase)) return null;

        var paymentId = message.QueryValue("data.id") ?? body?.Data?.Id;
        if (string.IsNullOrWhiteSpace(paymentId) || !paymentId.All(char.IsAsciiLetterOrDigit)) return null;
        if (!IsValidSignature(paymentId, message.Header(RequestIdHeader), message.Header(SignatureHeader)))
            return null;

        // Authentic notification: the payment itself is read from the API.
        using var request = Authorized(HttpMethod.Get, $"v1/payments/{paymentId}");
        var payment = await SendAsync<PaymentResponse>(request, "read the payment", cancellationToken);
        if (payment is null)
            // Unknown to Mercado Pago (e.g. the test notification of its panel): acknowledged, applies nothing.
            return new PaymentNotification($"{ReferencePrefix}{paymentId}_not_found",
                $"{ReferencePrefix}payment_{paymentId}", PaymentOutcome.Pending, 0, "PEN", "Payment not found.");

        var status = (payment.Status ?? "unknown").ToLowerInvariant();
        var reference = string.IsNullOrWhiteSpace(payment.ExternalReference) ||
                        payment.ExternalReference.Length > MaxReferenceLength
            ? $"{ReferencePrefix}payment_{paymentId}"
            : payment.ExternalReference;
        var outcome = status switch
        {
            "approved" => PaymentOutcome.Approved,
            "rejected" or "cancelled" => PaymentOutcome.Rejected,
            // pending, in_process, authorized, in_mediation, refunded, charged_back: no automatic change.
            _ => PaymentOutcome.Pending
        };
        var amountInCents = (long)Math.Round(payment.TransactionAmount * 100m, MidpointRounding.AwayFromZero);
        return new PaymentNotification($"{ReferencePrefix}{paymentId}_{status}", reference, outcome, amountInCents,
            payment.CurrencyId ?? string.Empty,
            outcome == PaymentOutcome.Approved ? null : $"Mercado Pago: {status} ({payment.StatusDetail})");
    }

    /// <summary>Signature Mercado Pago puts in <c>x-signature</c> (<c>v1</c>) for a notification.</summary>
    public static string Sign(byte[] secret, string paymentId, string? requestId, string timestamp)
    {
        var manifest = new StringBuilder($"id:{paymentId.ToLowerInvariant()};");
        if (!string.IsNullOrEmpty(requestId)) manifest.Append($"request-id:{requestId};");
        manifest.Append($"ts:{timestamp};");
        return Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(manifest.ToString())));
    }

    private bool IsValidSignature(string paymentId, string? requestId, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature) || _webhookSecret.Length == 0) return false;
        var parts = signature.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .GroupBy(pair => pair[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);
        if (!parts.TryGetValue("ts", out var timestamp) || !parts.TryGetValue("v1", out var received) ||
            string.IsNullOrEmpty(timestamp))
            return false;

        var expected = Encoding.ASCII.GetBytes(Sign(_webhookSecret, paymentId, requestId, timestamp));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(received.ToLowerInvariant()));
    }

    private string NotificationUrl()
    {
        // Only webhooks (not the legacy IPN feed) reach this address.
        var url = _options.NotificationUrl.Trim();
        return url + (url.Contains('?') ? "&" : "?") + "source_news=webhooks";
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        return message;
    }

    /// <returns>The body, or null when Mercado Pago answers 404.</returns>
    private async Task<T?> SendAsync<T>(HttpRequestMessage request, string action, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
                throw new PaymentGatewayUnavailableException(string.Create(CultureInfo.InvariantCulture,
                    $"Mercado Pago could not {action} (HTTP {(int)response.StatusCode})."));
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayUnavailableException($"Mercado Pago could not be reached to {action}.", ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayUnavailableException($"Mercado Pago gave an unexpected answer to {action}.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PaymentGatewayUnavailableException($"Mercado Pago did not answer in time to {action}.", ex);
        }
    }

    private static NotificationBody? ReadBody(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            return JsonSerializer.Deserialize<NotificationBody>(payload, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record PreferenceRequest(
        IReadOnlyList<PreferenceItem> Items,
        string ExternalReference,
        string NotificationUrl,
        string StatementDescriptor,
        BackUrls? BackUrls,
        string? AutoReturn,
        IReadOnlyDictionary<string, string> Metadata);

    private sealed record PreferenceItem(string Id, string Title, int Quantity, decimal UnitPrice, string CurrencyId);

    private sealed record BackUrls(string Success, string Failure, string Pending);

    private sealed record PreferenceResponse(string? Id, string? InitPoint);

    private sealed record PaymentResponse(
        long Id,
        string? Status,
        string? StatusDetail,
        string? ExternalReference,
        decimal TransactionAmount,
        string? CurrencyId);

    private sealed record NotificationBody(string? Type, NotificationData? Data);

    private sealed record NotificationData(string? Id);
}
