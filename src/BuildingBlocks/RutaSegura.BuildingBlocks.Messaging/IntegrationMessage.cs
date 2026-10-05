namespace RutaSegura.BuildingBlocks.Messaging;

/// <summary>Metadatos técnicos de integración. No es un evento interno de Domain.</summary>
public interface IIntegrationMessage
{
    Guid MessageId { get; }
    string MessageType { get; }
    int SchemaVersion { get; }
    DateTimeOffset OccurredAt { get; }
    string CorrelationId { get; }
}

/// <summary>Sobre inmutable; conservar MessageId al reintentar para permitir idempotencia.</summary>
public sealed record IntegrationMessage<TPayload> : IIntegrationMessage
{
    public IntegrationMessage(Guid messageId, string messageType, int schemaVersion,
        DateTimeOffset occurredAt, string correlationId, TPayload payload)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException("MessageId cannot be empty.", nameof(messageId));
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(payload);
        if (schemaVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (occurredAt == default)
            throw new ArgumentException("OccurredAt is required.", nameof(occurredAt));

        MessageId = messageId;
        MessageType = messageType;
        SchemaVersion = schemaVersion;
        OccurredAt = occurredAt.ToUniversalTime();
        CorrelationId = correlationId;
        Payload = payload;
    }

    public Guid MessageId { get; }
    public string MessageType { get; }
    public int SchemaVersion { get; }
    public DateTimeOffset OccurredAt { get; }
    public string CorrelationId { get; }
    public TPayload Payload { get; }
}
