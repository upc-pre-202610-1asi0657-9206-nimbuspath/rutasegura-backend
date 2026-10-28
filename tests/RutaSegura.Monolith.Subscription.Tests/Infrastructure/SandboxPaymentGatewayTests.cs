using Monolith.Subscription.Application.Internal.OutboundServices;
using Monolith.Subscription.Infrastructure.Payments.Sandbox;
using static RutaSegura.Monolith.Subscription.Tests.Support.SubscriptionMother;

namespace RutaSegura.Monolith.Subscription.Tests.Infrastructure;

/// <summary>The sandbox adapter verifies signatures like a real provider (HMAC-SHA256 over the exact body).</summary>
public sealed class SandboxPaymentGatewayTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly IPaymentGateway _gateway;
    private readonly SandboxPaymentGateway _sandbox = Sandbox();

    public SandboxPaymentGatewayTests() => _gateway = _sandbox;

    [Fact]
    public async Task Checkout_HasAUniqueReferenceAndTheConfiguredPage()
    {
        var request = new PaymentCheckoutRequest(Guid.NewGuid(), Organization, "COMPLETE", "Plan Completo", 9_990, "PEN");

        var first = await _gateway.CreateCheckoutAsync(request, Ct);
        var second = await _gateway.CreateCheckoutAsync(request, Ct);

        first.Reference.ShouldStartWith("sbx_");
        first.Reference.ShouldNotBe(second.Reference);
        first.CheckoutUrl.ShouldBe($"https://rutasegura.test/checkout/{first.Reference}");
    }

    [Fact]
    public void SignedNotification_IsReadBack()
    {
        var signed = _sandbox.Create("sbx_1", approved: true, 9_990, "PEN");

        var notification = _gateway.ReadNotification(signed.Payload, signed.Signature);

        notification.ShouldNotBeNull();
        notification.PaymentReference.ShouldBe("sbx_1");
        notification.Outcome.ShouldBe(PaymentOutcome.Approved);
        notification.AmountInCents.ShouldBe(9_990);
        notification.Currency.ShouldBe("PEN");
        notification.EventId.ShouldStartWith("evt_");
        signed.Signature.ShouldStartWith("sha256=");
    }

    [Fact]
    public void DeclinedNotification_CarriesItsReason()
    {
        var signed = _sandbox.Create("sbx_1", approved: false, 9_990, "PEN");

        var notification = _gateway.ReadNotification(signed.Payload, signed.Signature)!;

        notification.Outcome.ShouldBe(PaymentOutcome.Rejected);
        notification.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=00")]
    [InlineData("not-a-signature")]
    public void MissingOrWrongSignature_IsRejected(string? signature)
    {
        var signed = _sandbox.Create("sbx_1", approved: true, 9_990, "PEN");

        _gateway.ReadNotification(signed.Payload, signature).ShouldBeNull();
    }

    [Fact]
    public void SignatureOfAnotherSecret_IsRejected()
    {
        var foreign = Sandbox(secret: "a-different-secret-a-different-secret").Create("sbx_1", true, 9_990, "PEN");

        _gateway.ReadNotification(foreign.Payload, foreign.Signature).ShouldBeNull();
    }

    [Fact]
    public void SignedGarbage_IsRejected()
    {
        const string payload = "{\"hello\":\"world\"}";

        _gateway.ReadNotification(payload, _sandbox.Sign(payload)).ShouldBeNull();
        _gateway.ReadNotification("not json", _sandbox.Sign("not json")).ShouldBeNull();
    }

    [Fact]
    public void WithoutAConfiguredSecret_OnlyItsOwnNotificationsAreAccepted()
    {
        var first = Sandbox(secret: null);
        var second = Sandbox(secret: null);
        var signed = first.Create("sbx_1", true, 9_990, "PEN");

        ((IPaymentGateway)first).ReadNotification(signed.Payload, signed.Signature).ShouldNotBeNull();
        ((IPaymentGateway)second).ReadNotification(signed.Payload, signed.Signature).ShouldBeNull();
    }

    [Fact]
    public void SimulatedPayments_FollowTheConfiguration()
    {
        Sandbox(allowSimulatedPayments: true).Enabled.ShouldBeTrue();
        Sandbox(allowSimulatedPayments: false).Enabled.ShouldBeFalse();
    }
}
