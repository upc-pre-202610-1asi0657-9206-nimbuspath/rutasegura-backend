using System.Net;
using System.Text.Json;
using RutaSegura.Notification.Application.Internal.Diagnostics;
using RutaSegura.Notification.Domain.Model.ValueObjects;

namespace RutaSegura.Notification.Tests.Host;

public sealed class OperationsTests
{
    private const string MonitoringToken = "only-test-monitoring-token-0123456789abcdef";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MetricsAndOpenApiRequireTheSeparateOperationalSecret_EvenForAnAuthenticatedFamily()
    {
        await using var factory = new NotificationApiFactory(new() { ["NotificationMonitoring:Enabled"]="true", ["NotificationMonitoring:Token"]=MonitoringToken });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token());
        foreach (var path in new[] { "/metrics", "/openapi/v1.json" })
            (await client.GetAsync(path, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        client.DefaultRequestHeaders.Add("X-Metrics-Token", MonitoringToken);
        NotificationDiagnostics.EventProcessed("created", 1, 0.02);
        NotificationDiagnostics.DeliveryRecorded(NotificationChannel.Realtime, DeliveryStatus.ProviderAccepted, DateTimeOffset.UtcNow.AddSeconds(-2), DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow);
        using var metrics = await client.GetAsync("/metrics", Ct);
        metrics.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await metrics.Content.ReadAsStringAsync(Ct);
        text.ShouldContain("notification_events");
        text.ShouldContain("notification_event_to_acceptance");
        text.ShouldNotContain(factory.Organization.ToString());
        text.ShouldNotContain(MonitoringToken);
    }

    [Fact]
    public async Task OpenApiDocumentsTypedResponsesAndBearerSecurity_WithoutIncludingOperationalSecrets()
    {
        await using var factory = new NotificationApiFactory(new() { ["NotificationMonitoring:Token"]=MonitoringToken });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Metrics-Token", MonitoringToken);
        var text = await client.GetStringAsync("/openapi/v1.json", Ct);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString().ShouldBe("bearer");
        var paths = root.GetProperty("paths");
        var list = paths.GetProperty("/api/v1/notifications").GetProperty("get");
        list.GetProperty("security").GetArrayLength().ShouldBe(1);
        list.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("type").GetString().ShouldBe("array");
        list.GetProperty("responses").TryGetProperty("503", out _).ShouldBeTrue();
        paths.GetProperty("/api/v1/announcements").GetProperty("post").GetProperty("responses").TryGetProperty("201", out _).ShouldBeTrue();
        paths.TryGetProperty("/internal/v1/notifications/student-links", out _).ShouldBeFalse();
        text.ShouldNotContain(MonitoringToken);
        text.ShouldNotContain(NotificationApiFactory.Secret);
    }
}
