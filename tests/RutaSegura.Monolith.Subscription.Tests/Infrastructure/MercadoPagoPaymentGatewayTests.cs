using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Monolith.Subscription.Application.Internal.OutboundServices;
using Monolith.Subscription.Infrastructure.Payments;
using Monolith.Subscription.Infrastructure.Payments.MercadoPago;
using static RutaSegura.Monolith.Subscription.Tests.Support.SubscriptionMother;

namespace RutaSegura.Monolith.Subscription.Tests.Infrastructure;

/// <summary>
///     Mercado Pago Checkout Pro against a stubbed HTTP API: the preference sent, the verification of
///     <c>x-signature</c> and the translation of the payment read back. No test reaches Mercado Pago.
/// </summary>
public sealed class MercadoPagoPaymentGatewayTests
{
    private const string AccessToken = "TEST-0000000000000000-000000-unit-tests";
    private const string NotificationUrl = "https://api.rutasegura.test/api/v1/subscription/payments/notifications";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes(WebhookSecret);

    private readonly StubMercadoPago _api = new();
    private readonly IPaymentGateway _gateway;

    public MercadoPagoPaymentGatewayTests()
    {
        var options = Options.Create(new PaymentGatewayOptions
        {
            Provider = PaymentGatewayOptions.MercadoPagoProvider,
            WebhookSecret = WebhookSecret,
            MercadoPago = new MercadoPagoOptions
            {
                AccessToken = AccessToken,
                NotificationUrl = NotificationUrl,
                ReturnUrl = "https://app.rutasegura.test/subscription"
            }
        });
        _gateway = new MercadoPagoPaymentGateway(
            new HttpClient(_api) { BaseAddress = new Uri("https://api.mercadopago.test/") }, options);
    }

    [Fact]
    public async Task Checkout_IsAPreferenceWithThePlanChangeAsExternalReference()
    {
        var planChangeId = Guid.NewGuid();
        _api.Respond(HttpStatusCode.Created, """{"id":"123-pref","init_point":"https://www.mercadopago.test/checkout?pref_id=123-pref"}""");

        var checkout = await _gateway.CreateCheckoutAsync(
            new PaymentCheckoutRequest(planChangeId, Organization, "COMPLETE", "Plan Completo", 9_990, "PEN"), Ct);

        checkout.Reference.ShouldBe($"mp_{planChangeId:N}");
        checkout.CheckoutUrl.ShouldBe("https://www.mercadopago.test/checkout?pref_id=123-pref");

        var request = _api.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Path.ShouldBe("/checkout/preferences");
        request.Authorization.ShouldBe($"Bearer {AccessToken}");
        request.IdempotencyKey.ShouldBe(planChangeId.ToString());

        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        root.GetProperty("external_reference").GetString().ShouldBe(checkout.Reference);
        root.GetProperty("notification_url").GetString().ShouldBe($"{NotificationUrl}?source_news=webhooks");
        root.GetProperty("auto_return").GetString().ShouldBe("approved");
        var item = root.GetProperty("items")[0];
        item.GetProperty("id").GetString().ShouldBe("COMPLETE");
        item.GetProperty("quantity").GetInt32().ShouldBe(1);
        item.GetProperty("unit_price").GetDecimal().ShouldBe(99.90m);
        item.GetProperty("currency_id").GetString().ShouldBe("PEN");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Checkout_RefusedByMercadoPago_IsUnavailable(HttpStatusCode status)
    {
        _api.Respond(status, """{"message":"error"}""");

        await Should.ThrowAsync<PaymentGatewayUnavailableException>(() => _gateway.CreateCheckoutAsync(
            new PaymentCheckoutRequest(Guid.NewGuid(), Organization, "COMPLETE", "Plan Completo", 9_990, "PEN"), Ct));
    }

    [Fact]
    public async Task Checkout_WhenMercadoPagoIsUnreachable_IsUnavailable()
    {
        _api.Fail(new HttpRequestException("connection refused"));

        await Should.ThrowAsync<PaymentGatewayUnavailableException>(() => _gateway.CreateCheckoutAsync(
            new PaymentCheckoutRequest(Guid.NewGuid(), Organization, "COMPLETE", "Plan Completo", 9_990, "PEN"), Ct));
    }

    [Fact]
    public async Task SignedNotification_ReadsThePaymentFromTheApi()
    {
        _api.Respond(HttpStatusCode.OK, Payment(123, "approved", "accredited", "mp_abc", 99.9m));

        var notification = await _gateway.ReadNotificationAsync(Signed("123"), Ct);

        notification.ShouldNotBeNull();
        notification.EventId.ShouldBe("mp_123_approved");
        notification.PaymentReference.ShouldBe("mp_abc");
        notification.Outcome.ShouldBe(PaymentOutcome.Approved);
        notification.AmountInCents.ShouldBe(9_990);
        notification.Currency.ShouldBe("PEN");
        var request = _api.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Path.ShouldBe("/v1/payments/123");
        request.Authorization.ShouldBe($"Bearer {AccessToken}");
    }

    [Theory]
    [InlineData("rejected", PaymentOutcome.Rejected)]
    [InlineData("cancelled", PaymentOutcome.Rejected)]
    [InlineData("in_process", PaymentOutcome.Pending)]
    [InlineData("pending", PaymentOutcome.Pending)]
    [InlineData("refunded", PaymentOutcome.Pending)]
    public async Task PaymentStatus_IsTranslated(string status, PaymentOutcome expected)
    {
        _api.Respond(HttpStatusCode.OK, Payment(77, status, "cc_rejected_other_reason", "mp_abc", 99.9m));

        var notification = (await _gateway.ReadNotificationAsync(Signed("77"), Ct))!;

        notification.Outcome.ShouldBe(expected);
        notification.EventId.ShouldBe($"mp_77_{status}");
        notification.Reason.ShouldNotBeNull().ShouldContain(status);
    }

    [Fact]
    public async Task ForgedOrUnsignedNotification_IsRejectedWithoutCallingTheApi()
    {
        var forged = Signed("123", secret: Encoding.UTF8.GetBytes("another-secret-another-secret-123"));
        var unsigned = Signed("123") with { Headers = new Dictionary<string, string>() };
        var otherPayment = Signed("123") with { Query = Query("999") };

        (await _gateway.ReadNotificationAsync(forged, Ct)).ShouldBeNull();
        (await _gateway.ReadNotificationAsync(unsigned, Ct)).ShouldBeNull();
        (await _gateway.ReadNotificationAsync(otherPayment, Ct)).ShouldBeNull();
        _api.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("ts=1704908010")]
    [InlineData("v1=abc")]
    [InlineData("garbage")]
    public async Task MalformedSignatureHeader_IsRejected(string signature)
    {
        var message = Signed("123") with
        {
            Headers = new Dictionary<string, string> { ["x-signature"] = signature, ["x-request-id"] = "req-1" }
        };

        (await _gateway.ReadNotificationAsync(message, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task NotificationOfAnotherTopic_OrWithAnOddId_IsIgnored()
    {
        var order = Signed("123") with
        {
            Query = new Dictionary<string, string> { ["data.id"] = "123", ["type"] = "merchant_order" }
        };

        (await _gateway.ReadNotificationAsync(order, Ct)).ShouldBeNull();
        (await _gateway.ReadNotificationAsync(Signed("../admin"), Ct)).ShouldBeNull();
        _api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task SignatureUsesTheLowercaseId_AndWorksWithoutRequestId()
    {
        _api.Respond(HttpStatusCode.OK, Payment(5, "approved", "accredited", "mp_abc", 99.9m));
        const string timestamp = "1704908010";
        var message = new PaymentNotificationMessage("{}",
            new Dictionary<string, string>
            {
                ["X-Signature"] = $"ts={timestamp},v1={MercadoPagoPaymentGateway.Sign(Secret, "ab5", null, timestamp)}"
            },
            Query("AB5"));

        (await _gateway.ReadNotificationAsync(message, Ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task PaymentUnknownToMercadoPago_IsAcknowledgedAsUnmatched()
    {
        _api.Respond(HttpStatusCode.NotFound, """{"message":"Payment not found"}""");

        var notification = (await _gateway.ReadNotificationAsync(Signed("123456"), Ct))!;

        notification.Outcome.ShouldBe(PaymentOutcome.Pending);
        notification.PaymentReference.ShouldBe("mp_payment_123456");
    }

    [Fact]
    public async Task PaymentWithoutExternalReference_DoesNotMatchAnyChange()
    {
        _api.Respond(HttpStatusCode.OK, Payment(9, "approved", "accredited", null, 99.9m));

        (await _gateway.ReadNotificationAsync(Signed("9"), Ct))!.PaymentReference.ShouldBe("mp_payment_9");
    }

    [Fact]
    public async Task ApiDown_WhileReadingThePayment_IsUnavailable()
    {
        _api.Respond(HttpStatusCode.BadGateway, "");

        await Should.ThrowAsync<PaymentGatewayUnavailableException>(() =>
            _gateway.ReadNotificationAsync(Signed("123"), Ct));
    }

    /// <summary>A webhook as Mercado Pago sends it: body, <c>x-signature</c>, <c>x-request-id</c> and <c>data.id</c>.</summary>
    private static PaymentNotificationMessage Signed(string paymentId, byte[]? secret = null)
    {
        const string requestId = "bb56a2f1-6aae-46ac-982e-9dcd3581d08e";
        const string timestamp = "1704908010";
        var v1 = MercadoPagoPaymentGateway.Sign(secret ?? Secret, paymentId, requestId, timestamp);
        var payload = $$"""{"action":"payment.updated","api_version":"v1","data":{"id":"{{paymentId}}"},"live_mode":false,"type":"payment"}""";
        return new PaymentNotificationMessage(payload,
            new Dictionary<string, string>
            {
                ["x-signature"] = $"ts={timestamp},v1={v1}",
                ["x-request-id"] = requestId
            },
            Query(paymentId));
    }

    private static Dictionary<string, string> Query(string paymentId) =>
        new() { ["data.id"] = paymentId, ["type"] = "payment" };

    private static string Payment(long id, string status, string detail, string? reference, decimal amount) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["status"] = status,
            ["status_detail"] = detail,
            ["external_reference"] = reference,
            ["transaction_amount"] = amount,
            ["currency_id"] = "PEN"
        });

    /// <summary>Records each request and answers with the configured response.</summary>
    private sealed class StubMercadoPago : HttpMessageHandler
    {
        private Func<HttpResponseMessage> _respond = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        public List<RecordedRequest> Requests { get; } = [];

        public void Respond(HttpStatusCode status, string body) =>
            _respond = () => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

        public void Fail(Exception exception) => _respond = () => throw exception;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Idempotency-Key", out var keys) ? keys.Single() : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _respond();
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string? Authorization,
        string? IdempotencyKey,
        string? Body);
}
