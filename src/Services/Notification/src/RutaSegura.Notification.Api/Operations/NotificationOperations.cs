using System.Security.Cryptography;
using System.Text;
using OpenTelemetry.Metrics;
using RutaSegura.Notification.Application.Internal.Diagnostics;
using RutaSegura.Notification.Infrastructure.Channels;

namespace RutaSegura.Notification.Api.Operations;

public static class NotificationOperations
{
    public static bool Authorized(HttpContext context, IConfiguration configuration)
    {
        var expected = configuration["NotificationMonitoring:Token"] ?? "";
        var received = context.Request.Headers["X-Metrics-Token"].ToString();
        return expected.Length >= 32 && received.Length is >= 32 and <= 1024 &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(received)));
    }

    public static IServiceCollection AddNotificationMonitoring(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("NotificationMonitoring:Enabled")) return services;
        if ((configuration["NotificationMonitoring:Token"] ?? "").Length < 32)
            throw new InvalidOperationException("Configura NotificationMonitoring:Token de al menos 32 caracteres.");
        services.AddOpenTelemetry().WithMetrics(metrics => metrics
            .AddMeter(NotificationDiagnostics.MeterName, NotificationBacklogMonitor.MeterName)
            .AddView("notification.event.processing", new ExplicitBucketHistogramConfiguration { Boundaries = [0.01, 0.05, 0.1, 0.5, 1, 3, 5, 10, 30] })
            .AddView("notification.dispatch.latency", new ExplicitBucketHistogramConfiguration { Boundaries = [0.1, 0.5, 1, 3, 5, 10, 30, 60, 300] })
            .AddView("notification.event.to.acceptance", new ExplicitBucketHistogramConfiguration { Boundaries = [0.1, 0.5, 1, 3, 5, 10, 30, 60, 300] })
            .AddPrometheusExporter());
        if (configuration.GetValue<bool>("NotificationApi:Enabled") || configuration.GetValue<bool>("NotificationRabbitMq:Enabled"))
            services.AddHostedService<NotificationBacklogMonitor>();
        return services;
    }
}
