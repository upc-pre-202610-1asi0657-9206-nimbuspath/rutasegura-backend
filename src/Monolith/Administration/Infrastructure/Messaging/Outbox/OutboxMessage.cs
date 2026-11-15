namespace Monolith.Administration.Infrastructure.Messaging.Outbox;

/// <summary>
///     Integration event stored in the same transaction as the business change (Transactional Outbox, PT-04)
///     and published to RabbitMQ afterwards by <see cref="OutboxPublisherService" />.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;

    /// <summary>camelCase JSON of the <c>*V1</c> contract.</summary>
    public string Payload { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}
