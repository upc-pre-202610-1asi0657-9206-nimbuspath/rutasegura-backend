using System.Diagnostics.Metrics;
using RutaSegura.Notification.Domain.Model.ValueObjects;

namespace RutaSegura.Notification.Application.Internal.Diagnostics;

/// <summary>Metricas operativas de cardinalidad acotada. Nunca contienen ids, tokens, alumnos ni organizaciones.</summary>
public static class NotificationDiagnostics
{
    public const string MeterName = "RutaSegura.Notification";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> Events = Meter.CreateCounter<long>("notification.events", "{event}");
    private static readonly Counter<long> Notices = Meter.CreateCounter<long>("notification.notices.created", "{notice}");
    private static readonly Histogram<double> Processing = Meter.CreateHistogram<double>("notification.event.processing", "s");
    private static readonly Counter<long> Transfers = Meter.CreateCounter<long>("notification.broker.transfers", "{message}");
    private static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("notification.delivery.attempts", "{attempt}");
    private static readonly Histogram<double> QueueLatency = Meter.CreateHistogram<double>("notification.dispatch.latency", "s");
    private static readonly Histogram<double> EventLatency = Meter.CreateHistogram<double>("notification.event.to.acceptance", "s");

    public static void EventProcessed(string outcome, int created, double seconds)
    {
        Events.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        if (created > 0) Notices.Add(created);
        Processing.Record(seconds, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static void Transferred(bool retry) => Transfers.Add(1, new KeyValuePair<string, object?>("destination", retry ? "retry" : "dlq"));

    // Solo se llama despues del commit del resultado del canal. Aceptacion no significa lectura ni recepcion en dispositivo.
    public static void DeliveryRecorded(NotificationChannel channel, DeliveryStatus status, DateTimeOffset occurred,
        DateTimeOffset created, DateTimeOffset committed)
    {
        var channelTag = new KeyValuePair<string, object?>("channel", channel == NotificationChannel.Push ? "push" : "realtime");
        Deliveries.Add(1, channelTag, new("outcome", status.ToString()));
        if (status != DeliveryStatus.ProviderAccepted) return;
        if (committed >= created) QueueLatency.Record((committed - created).TotalSeconds, channelTag);
        // Los relojes del movil pueden estar adelantados; no registrar latencias negativas ni inventar un cero.
        if (committed >= occurred) EventLatency.Record((committed - occurred).TotalSeconds, channelTag);
    }
}
