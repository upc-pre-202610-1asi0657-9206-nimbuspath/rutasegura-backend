namespace Monolith.Administration.Infrastructure.Messaging;

/// <summary>
///     RabbitMQ and outbox settings of the Administration bounded context (section <c>Administration:Messaging</c>).
///     With <see cref="Enabled" /> = false (development without a broker) integration events still go to the
///     outbox table and are published as soon as messaging is enabled.
/// </summary>
public sealed class AdministrationMessagingOptions
{
    public const string Section = "Administration:Messaging";

    public bool Enabled { get; set; }

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ClientName { get; set; } = "monolith-administration";

    /// <summary>Topic exchange where Administration publishes (Trip &amp; Tracking binds <c>*.route-*</c> here).</summary>
    public string EventsExchange { get; set; } = "administration.events";

    /// <summary>Durable queue with the Trip &amp; Tracking events Administration consumes (+ <c>.dlq</c>).</summary>
    public string InboundQueue { get; set; } = "administration.inbound";

    public ushort PrefetchCount { get; set; } = 20;

    /// <summary>Subscriptions of the inbound queue; when empty the four Trip &amp; Tracking events are used.</summary>
    public List<RabbitMqBinding> InboundBindings { get; set; } = [];

    public TimeSpan OutboxPollingInterval { get; set; } = TimeSpan.FromSeconds(1);
    public int OutboxBatchSize { get; set; } = 50;
    public TimeSpan OutboxMaxBackoff { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan OutboxRetention { get; set; } = TimeSpan.FromDays(7);

    public IReadOnlyList<RabbitMqBinding> EffectiveInboundBindings => InboundBindings.Count > 0
        ? InboundBindings
        :
        [
            new RabbitMqBinding { Exchange = "trip-tracking.events", RoutingKey = "trip-tracking.incident-reported" },
            new RabbitMqBinding { Exchange = "trip-tracking.events", RoutingKey = "trip-tracking.sos-raised" },
            new RabbitMqBinding { Exchange = "trip-tracking.events", RoutingKey = "trip-tracking.sos-resolved" },
            new RabbitMqBinding { Exchange = "trip-tracking.events", RoutingKey = "trip-tracking.trip-completed" }
        ];
}

public sealed class RabbitMqBinding
{
    public string Exchange { get; set; } = string.Empty;
    public string RoutingKey { get; set; } = "#";
}
