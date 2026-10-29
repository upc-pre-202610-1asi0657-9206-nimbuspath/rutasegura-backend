using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Monolith.Subscription.Domain.Model.Aggregates;
using RutaSegura.Monolith.Administration.IntegrationTests.Support;

namespace RutaSegura.Monolith.Administration.IntegrationTests;

/// <summary>
///     US-07 through the real monolith: plan catalog, plan change paid through the (sandbox) gateway, signed
///     notifications and the limit Route Planning enforces when it activates routes.
/// </summary>
public sealed class SubscriptionEndToEndTests : IDisposable
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly MonolithApp _app = new(maxActiveRoutes: 1);

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task PlanCatalog_IsPublic()
    {
        var plans = await _app.CreateClient().Get("/api/v1/subscription/plans").ExpectAsync(HttpStatusCode.OK);

        plans.EnumerateArray().Select(p => p.GetProperty("planId").GetString())
            .ShouldBe(["BASIC", "COMPLETE", "PREMIUM"]);
        plans[1].GetProperty("monthlyPrice").GetDecimal().ShouldBe(99.90m);
        plans[1].GetProperty("currency").GetString().ShouldBe("PEN");
    }

    [Fact]
    public async Task PaidPlan_RaisesTheRouteLimitOnlyAfterThePaymentIsVerified()
    {
        var admin = await _app.AdminAsync();
        var driverId = await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver);
        var vehicleId = await admin.RegisterVehicleAsync();
        var first = await admin.ReadyRouteAsync(driverId, vehicleId, "Ruta 1", "06:00:00", "06:40:00");
        var second = await admin.ReadyRouteAsync(driverId, vehicleId, "Ruta 2", "07:00:00", "07:40:00");
        await admin.PostJson($"/api/v1/routes/{first}/activate").ExpectAsync(HttpStatusCode.OK);
        (await admin.PostJson($"/api/v1/routes/{second}/activate").ExpectAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("PlanRouteLimitReached");

        // US-07 S1: the change waits for its payment and the limit in force does not move.
        var response = await admin.PostAsJsonAsync("/api/v1/subscription/plan-changes",
            new { targetPlanId = "COMPLETE" }, MonolithApp.Json, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location.ShouldNotBeNull();
        var change = await response.ReadJsonAsync();
        change.GetProperty("status").GetString().ShouldBe("PENDING_PAYMENT");
        change.GetProperty("targetPlanId").GetString().ShouldBe("COMPLETE");
        change.GetProperty("amount").GetDecimal().ShouldBe(99.90m);
        var reference = change.GetProperty("paymentReference").GetString()!;
        (await Quota(admin)).GetProperty("maxActiveRoutes").GetInt32().ShouldBe(1);

        // US-07 S2: the verified payment applies the plan; Route Planning sees the new limit at once.
        var receipt = await admin.PostJson($"/api/v1/subscription/sandbox/checkouts/{reference}/complete",
            new { approved = true }).ExpectAsync(HttpStatusCode.OK);
        receipt.GetProperty("resolution").GetString().ShouldBe("APPLIED");
        var quota = await Quota(admin);
        quota.GetProperty("planCode").GetString().ShouldBe("COMPLETE");
        quota.GetProperty("maxActiveRoutes").GetInt32().ShouldBe(10);
        await admin.PostJson($"/api/v1/routes/{second}/activate").ExpectAsync(HttpStatusCode.OK);

        var subscription = await admin.Get("/api/v1/subscription").ExpectAsync(HttpStatusCode.OK);
        subscription.GetProperty("effectivePlan").GetProperty("planId").GetString().ShouldBe("COMPLETE");
        subscription.GetProperty("paidUntil").ValueKind.ShouldBe(JsonValueKind.String);
        subscription.GetProperty("pendingChange").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GatewayNotification_IsVerifiedAndAppliedOnlyOnce()
    {
        var admin = await _app.AdminAsync();
        var reference = await RequestCompleteAsync(admin);
        var gateway = _app.CreateClient();
        var body = Notification("evt_it_1", reference, "APPROVED", 9_990);

        // A body that is not signed by the gateway changes nothing.
        (await PostNotification(gateway, body, "sha256=" + new string('0', 64)).ExpectAsync(HttpStatusCode.Unauthorized))
            .Code().ShouldBe("PaymentNotificationInvalid");
        (await PostNotification(gateway, body, signature: null).ExpectAsync(HttpStatusCode.Unauthorized))
            .Code().ShouldBe("PaymentNotificationInvalid");
        (await Quota(admin)).GetProperty("planCode").GetString().ShouldBe("BASIC");

        // US-07 S3: the gateway retries the same notification; it is applied once.
        (await PostNotification(gateway, body, Sign(body)).ExpectAsync(HttpStatusCode.OK))
            .GetProperty("resolution").GetString().ShouldBe("APPLIED");
        (await PostNotification(gateway, body, Sign(body)).ExpectAsync(HttpStatusCode.OK))
            .GetProperty("resolution").GetString().ShouldBe("DUPLICATE");

        var events = await _app.WithDbAsync(db => db.Set<ProcessedPaymentEvent>().ToListAsync());
        events.ShouldHaveSingleItem().Resolution.ShouldBe("APPLIED");
        (await Quota(admin)).GetProperty("planCode").GetString().ShouldBe("COMPLETE");
    }

    [Fact]
    public async Task DeclinedOrUnderpaidPayment_KeepsTheLimitInForce()
    {
        var admin = await _app.AdminAsync();
        var gateway = _app.CreateClient();

        var declined = await RequestCompleteAsync(admin);
        var body = Notification("evt_it_declined", declined, "REJECTED", 9_990);
        (await PostNotification(gateway, body, Sign(body)).ExpectAsync(HttpStatusCode.OK))
            .GetProperty("resolution").GetString().ShouldBe("REJECTED");

        var underpaid = await RequestCompleteAsync(admin);
        body = Notification("evt_it_underpaid", underpaid, "APPROVED", 100);
        (await PostNotification(gateway, body, Sign(body)).ExpectAsync(HttpStatusCode.OK))
            .GetProperty("resolution").GetString().ShouldBe("AMOUNT_MISMATCH");

        (await Quota(admin)).GetProperty("planCode").GetString().ShouldBe("BASIC");
    }

    [Fact]
    public async Task SecondChangeWhileOneIsPending_Returns409()
    {
        var admin = await _app.AdminAsync();
        var reference = await RequestCompleteAsync(admin);

        (await admin.PostJson("/api/v1/subscription/plan-changes", new { targetPlanId = "PREMIUM" })
            .ExpectAsync(HttpStatusCode.Conflict)).Code().ShouldBe("PlanChangeAlreadyPending");

        var pending = await admin.Get("/api/v1/subscription").ExpectAsync(HttpStatusCode.OK);
        pending.GetProperty("pendingChange").GetProperty("paymentReference").GetString().ShouldBe(reference);
        var changeId = pending.GetProperty("pendingChange").GetProperty("planChangeId").GetGuid();
        (await admin.PostJson($"/api/v1/subscription/plan-changes/{changeId}/cancel").ExpectAsync(HttpStatusCode.OK))
            .GetProperty("status").GetString().ShouldBe("CANCELLED");
        await admin.PostJson("/api/v1/subscription/plan-changes", new { targetPlanId = "PREMIUM" })
            .ExpectAsync(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task OnlyAdministratorsManageTheContract()
    {
        var driver = await _app.SignInAsync(SeedAccounts.DriverEmail, SeedAccounts.DriverPassword);
        var parent = await _app.SignInAsync(SeedAccounts.ParentEmail, SeedAccounts.ParentPassword);

        (await driver.Get("/api/v1/subscription")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await parent.PostJson("/api/v1/subscription/plan-changes", new { targetPlanId = "COMPLETE" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _app.CreateClient().Get("/api/v1/subscription")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SimulatedPayments_CanBeDisabled()
    {
        using var app = new MonolithApp(settings: new Dictionary<string, string?>
        {
            ["Subscription:PaymentGateway:AllowSimulatedPayments"] = "false"
        });
        var admin = await app.AdminAsync();
        var reference = await RequestCompleteAsync(admin);

        (await admin.PostJson($"/api/v1/subscription/sandbox/checkouts/{reference}/complete", new { approved = true })
            .ExpectAsync(HttpStatusCode.Forbidden)).Code().ShouldBe("SimulatedPaymentsDisabled");
    }

    [Fact]
    public void MercadoPago_WithoutCredentials_RefusesToStart()
    {
        using var app = new MonolithApp(settings: new Dictionary<string, string?>
        {
            ["Subscription:PaymentGateway:Provider"] = "MercadoPago"
        });

        var error = Should.Throw<InvalidOperationException>(() => app.CreateClient());
        error.Message.ShouldContain("MercadoPago:AccessToken");
        error.Message.ShouldContain("MercadoPago:NotificationUrl");
    }

    [Fact]
    public async Task MercadoPago_VerifiesTheWebhookAndNeverSimulatesPayments()
    {
        using var app = new MonolithApp(settings: new Dictionary<string, string?>
        {
            ["Subscription:PaymentGateway:Provider"] = "MercadoPago",
            ["Subscription:PaymentGateway:MercadoPago:AccessToken"] = "TEST-integration-tests",
            ["Subscription:PaymentGateway:MercadoPago:NotificationUrl"] =
                "https://api.rutasegura.test/api/v1/subscription/payments/notifications",
            // Nothing listens there: the tests never reach Mercado Pago.
            ["Subscription:PaymentGateway:MercadoPago:ApiBaseUrl"] = "http://127.0.0.1:9/",
            ["Subscription:PaymentGateway:MercadoPago:TimeoutSeconds"] = "2"
        });
        var admin = await app.AdminAsync();
        var gateway = app.CreateClient();

        // The checkout cannot be created: the change is cancelled and the plan in force stays.
        (await admin.PostJson("/api/v1/subscription/plan-changes", new { targetPlanId = "COMPLETE" })
            .ExpectAsync(HttpStatusCode.ServiceUnavailable)).Code().ShouldBe("PaymentGatewayUnavailable");
        (await admin.PostJson("/api/v1/subscription/sandbox/checkouts/mp_x/complete", new { approved = true })
            .ExpectAsync(HttpStatusCode.Forbidden)).Code().ShouldBe("SimulatedPaymentsDisabled");

        // A forged webhook is refused; an authentic one whose payment cannot be read is retried later (503).
        const string paymentId = "123456";
        const string requestId = "req-it-1";
        const string ts = "1704908010";
        var forged = MercadoPagoWebhook(paymentId, $"ts={ts},v1={new string('0', 64)}", requestId);
        (await gateway.SendAsync(forged, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var v1 = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(MonolithApp.TestPaymentWebhookSecret),
            Encoding.UTF8.GetBytes($"id:{paymentId};request-id:{requestId};ts:{ts};")));
        var authentic = MercadoPagoWebhook(paymentId, $"ts={ts},v1={v1}", requestId);
        (await gateway.SendAsync(authentic, Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        (await app.WithDbAsync(db => db.Set<ProcessedPaymentEvent>().CountAsync())).ShouldBe(0);
        (await Quota(admin)).GetProperty("planCode").GetString().ShouldBe("BASIC");
    }

    private static HttpRequestMessage MercadoPagoWebhook(string paymentId, string signature, string requestId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/subscription/payments/notifications?data.id={paymentId}&type=payment")
        {
            Content = new StringContent(
                $$"""{"action":"payment.updated","data":{"id":"{{paymentId}}"},"type":"payment"}""",
                Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-signature", signature);
        request.Headers.Add("x-request-id", requestId);
        return request;
    }

    private static async Task<string> RequestCompleteAsync(HttpClient admin) =>
        (await admin.PostJson("/api/v1/subscription/plan-changes", new { targetPlanId = "COMPLETE" })
            .ExpectAsync(HttpStatusCode.Accepted)).GetProperty("paymentReference").GetString()!;

    private static Task<JsonElement> Quota(HttpClient admin) =>
        admin.Get("/api/v1/routes/quota").ExpectAsync(HttpStatusCode.OK);

    /// <summary>A notification in the format of the sandbox gateway.</summary>
    private static string Notification(string eventId, string reference, string status, long amountInCents) =>
        JsonSerializer.Serialize(new { eventId, reference, status, amountInCents, currency = "PEN" });

    private static string Sign(string body) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(MonolithApp.TestPaymentWebhookSecret), Encoding.UTF8.GetBytes(body)));

    private static Task<HttpResponseMessage> PostNotification(HttpClient gateway, string body, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/subscription/payments/notifications")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (signature is not null) request.Headers.Add("X-Payment-Signature", signature);
        return gateway.SendAsync(request, Ct);
    }
}
