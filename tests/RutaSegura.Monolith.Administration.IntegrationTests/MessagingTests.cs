using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Monolith.Administration.Infrastructure.Messaging;
using Monolith.Administration.Infrastructure.Messaging.Inbound;
using Monolith.Administration.Infrastructure.Messaging.Outbox;
using RutaSegura.Monolith.Administration.IntegrationTests.Support;

namespace RutaSegura.Monolith.Administration.IntegrationTests;

/// <summary>
///     Messaging with Trip &amp; Tracking without a broker: the outbox relay (with a fake publisher) and the inbound
///     processor the RabbitMQ consumer delegates to (Inbox, translation, use case, single commit).
/// </summary>
public sealed class MessagingTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly MonolithApp _app = new();

    public void Dispose() => _app.Dispose();

    // ── Outbox → RabbitMQ ────────────────────────────────────────────────────

    [Fact]
    public async Task OutboxRelay_PublishesPendingMessagesOnceInOrder()
    {
        var admin = await _app.AdminAsync();
        var routeId = await admin.ReadyRouteAsync(await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver),
            await admin.RegisterVehicleAsync());
        await admin.PostJson($"/api/v1/routes/{routeId}/activate").ExpectAsync(HttpStatusCode.OK);
        var publisher = new RecordingPublisher();
        var relay = Relay(publisher);

        (await relay.ProcessBatchAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
        (await relay.ProcessBatchAsync(TestContext.Current.CancellationToken)).ShouldBe(0);

        publisher.Published.Select(m => m.MessageType)
            .ShouldBe(["administration.route-assigned", "administration.route-published"], ignoreOrder: false);
        var pending = await _app.WithDbAsync(db => db.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt == null));
        pending.ShouldBe(0);
    }

    [Fact]
    public async Task OutboxRelay_BrokerDown_KeepsTheMessageAndSchedulesARetry()
    {
        var admin = await _app.AdminAsync();
        await admin.ReadyRouteAsync(await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver),
            await admin.RegisterVehicleAsync());

        await Relay(new RecordingPublisher { Fail = true }).ProcessBatchAsync(TestContext.Current.CancellationToken);

        var message = await _app.WithDbAsync(db => db.Set<OutboxMessage>().SingleAsync());
        message.ProcessedAt.ShouldBeNull();
        message.Attempts.ShouldBe(1);
        message.NextAttemptAt.ShouldNotBeNull();
        message.LastError!.ShouldContain("broker unavailable");
    }

    // ── Trip & Tracking → Administration ─────────────────────────────────────

    [Fact]
    public async Task IncidentReported_IsProcessedOnceAndVisibleToTheAdministrator()
    {
        var admin = await _app.AdminAsync();
        var messageId = Guid.NewGuid().ToString();
        var body = Body(new
        {
            tripId = Guid.NewGuid(), routeId = Guid.NewGuid(), organizationId = SeedAccounts.Organization,
            routeName = "Ruta 3", incidentId = Guid.NewGuid(), type = "VehicleFailure", detail = "Llanta baja",
            notifyGuardians = true, position = new { latitude = -12.11, longitude = -76.98 },
            affectedStudents = new[] { Guid.NewGuid(), Guid.NewGuid() }, occurredAt = DateTimeOffset.UtcNow
        });

        (await HandleAsync(messageId, "trip-tracking.incident-reported", body)).ShouldBe(InboundOutcome.Processed);
        (await HandleAsync(messageId, "trip-tracking.incident-reported", body)).ShouldBe(InboundOutcome.Duplicate);

        var alerts = await admin.Get("/api/v1/operations/alerts").ExpectAsync(HttpStatusCode.OK);
        alerts.GetArrayLength().ShouldBe(1);
        alerts[0].GetProperty("severity").GetString().ShouldBe("HIGH");
        alerts[0].GetProperty("category").GetString().ShouldBe("VEHICLE_FAILURE");
        alerts[0].GetProperty("affectedStudents").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task SosRaisedAndResolved_TheAlertIsClosedFromTripTracking()
    {
        var admin = await _app.AdminAsync();
        var tripId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var raised = Body(new
        {
            tripId, routeId, organizationId = SeedAccounts.Organization, routeName = "Ruta 3",
            driverId = SeedAccounts.Driver, vehicleId = Guid.NewGuid(), position = (object?)null,
            studentsOnBoard = new[] { Guid.NewGuid() }, occurredAt = DateTimeOffset.UtcNow
        });
        var resolved = Body(new
        {
            tripId, routeId, organizationId = SeedAccounts.Organization, resolvedBy = SeedAccounts.Admin,
            occurredAt = DateTimeOffset.UtcNow
        });

        (await HandleAsync(Guid.NewGuid().ToString(), "trip-tracking.sos-raised", raised))
            .ShouldBe(InboundOutcome.Processed);
        var open = await admin.Get("/api/v1/operations/alerts").ExpectAsync(HttpStatusCode.OK);
        open[0].GetProperty("severity").GetString().ShouldBe("CRITICAL");

        // An administrator can acknowledge an SOS but not close it: Trip & Tracking owns its resolution.
        var alertId = open[0].Id();
        await admin.PostJson($"/api/v1/operations/alerts/{alertId}/acknowledge").ExpectAsync(HttpStatusCode.OK);
        (await admin.PostJson($"/api/v1/operations/alerts/{alertId}/resolve").ExpectAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("AlertInvalidTransition");

        (await HandleAsync(Guid.NewGuid().ToString(), "trip-tracking.sos-resolved", resolved))
            .ShouldBe(InboundOutcome.Processed);
        (await admin.Get("/api/v1/operations/alerts").ExpectAsync(HttpStatusCode.OK)).GetArrayLength().ShouldBe(0);
        (await admin.Get("/api/v1/operations/alerts?onlyOpen=false").ExpectAsync(HttpStatusCode.OK))[0]
            .GetProperty("status").GetString().ShouldBe("RESOLVED");
    }

    [Fact]
    public async Task TripCompleted_IsRecordedForTheRoute()
    {
        var admin = await _app.AdminAsync();
        var routeId = await admin.CreateRouteAsync();
        var body = Body(new
        {
            tripId = Guid.NewGuid(), routeId, organizationId = (Guid?)null, routeName = "Ruta 3",
            deliveredStudents = new[] { Guid.NewGuid(), Guid.NewGuid() }, absentStudents = new[] { Guid.NewGuid() },
            incidentCount = 1, startedAt = DateTimeOffset.UtcNow.AddMinutes(-65), completedAt = DateTimeOffset.UtcNow
        });

        // The event carries no organization: the organization of the planned route is used.
        (await HandleAsync(Guid.NewGuid().ToString(), "trip-tracking.trip-completed", body))
            .ShouldBe(InboundOutcome.Processed);

        var records = await admin.Get($"/api/v1/routes/{routeId}/trip-records").ExpectAsync(HttpStatusCode.OK);
        records.GetArrayLength().ShouldBe(1);
        records[0].GetProperty("deliveredStudents").GetInt32().ShouldBe(2);
        records[0].GetProperty("durationMinutes").GetDouble().ShouldBe(65, tolerance: 0.1);
    }

    [Theory]
    [InlineData(null, "trip-tracking.incident-reported", "{}", InboundOutcome.Rejected)]          // no message id
    [InlineData("valid", "trip-tracking.incident-reported", "{not json", InboundOutcome.Rejected)] // unreadable body
    [InlineData("valid", "trip-tracking.student-boarded", "{}", InboundOutcome.Ignored)]           // not for us
    [InlineData("valid", "trip-tracking.trip-completed", "{\"routeId\":\"00000000-0000-0000-0000-000000000000\"}",
        InboundOutcome.Rejected)]                                                                      // invalid data
    public async Task InvalidOrForeignMessages_AreRejectedOrIgnored(string? messageId, string type, string body,
        InboundOutcome expected)
    {
        var id = messageId is null ? null : Guid.NewGuid().ToString();

        (await HandleAsync(id, type, Encoding.UTF8.GetBytes(body))).ShouldBe(expected);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static byte[] Body(object payload) => JsonSerializer.SerializeToUtf8Bytes(payload, Json);

    private async Task<InboundOutcome> HandleAsync(string? messageId, string type, byte[] body)
    {
        var outcome = InboundOutcome.Retry;
        await _app.WithScopeAsync(async services =>
        {
            var processor = services.GetRequiredService<InboundMessageProcessor>();
            outcome = await processor.HandleAsync(messageId, type, body, false, TestContext.Current.CancellationToken);
        });
        return outcome;
    }

    private OutboxPublisherService Relay(IOutboxMessagePublisher publisher) =>
        new(_app.Services.GetRequiredService<IServiceScopeFactory>(), publisher,
            Options.Create(new AdministrationMessagingOptions()), TimeProvider.System,
            NullLogger<OutboxPublisherService>.Instance);

    private sealed class RecordingPublisher : IOutboxMessagePublisher
    {
        public bool Fail { get; init; }
        public List<OutboxMessage> Published { get; } = [];

        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("broker unavailable");
            Published.Add(message);
            return Task.CompletedTask;
        }
    }
}
