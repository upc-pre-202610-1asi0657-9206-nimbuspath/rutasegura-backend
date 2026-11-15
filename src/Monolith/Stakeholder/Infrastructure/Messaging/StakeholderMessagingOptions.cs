namespace Monolith.Stakeholder.Infrastructure.Messaging;

/// <summary>
///     RabbitMQ and outbox settings of Stakeholder (section <c>Stakeholder:Messaging</c>). With <see cref="Enabled" />
///     = false events stay in the outbox table and are published once messaging is enabled.
/// </summary>
public sealed class StakeholderMessagingOptions
{
    public const string Section = "Stakeholder:Messaging";

    public bool Enabled { get; set; }

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ClientName { get; set; } = "monolith-stakeholder";

    /// <summary>Topic exchange where Stakeholder publishes (Trip &amp; Tracking binds <c>*.guardian-student-*</c>).</summary>
    public string EventsExchange { get; set; } = "stakeholder.events";

    public TimeSpan OutboxPollingInterval { get; set; } = TimeSpan.FromSeconds(1);
    public int OutboxBatchSize { get; set; } = 50;
    public TimeSpan OutboxMaxBackoff { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan OutboxRetention { get; set; } = TimeSpan.FromDays(7);
}
