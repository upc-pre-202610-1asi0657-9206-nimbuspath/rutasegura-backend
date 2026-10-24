namespace Monolith.Administration.Infrastructure.Messaging.Inbound;

/// <summary>Idempotent Consumer (PT-04): a message id received from the broker is processed only once.</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}
