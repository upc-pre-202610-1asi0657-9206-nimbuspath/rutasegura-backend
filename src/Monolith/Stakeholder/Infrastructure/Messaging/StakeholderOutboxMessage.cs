namespace Monolith.Stakeholder.Infrastructure.Messaging;

/// <summary>
///     Integration event stored by the same commit as the change (Transactional Outbox, PT-04) and published to
///     <c>stakeholder.events</c> afterwards by <see cref="StakeholderOutboxPublisherService" />.
/// </summary>
public sealed class StakeholderOutboxMessage
{
    public Guid Id { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string Payload { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}
