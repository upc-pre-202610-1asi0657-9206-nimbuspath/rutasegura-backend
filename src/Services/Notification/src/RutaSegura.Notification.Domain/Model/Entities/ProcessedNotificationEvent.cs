using RutaSegura.BuildingBlocks.Domain;

namespace RutaSegura.Notification.Domain.Model.Entities;

/// <summary>Inbox durable. Se agrega solo al completar el caso de uso, en la misma transaccion que sus avisos.</summary>
public sealed class ProcessedNotificationEvent : Entity<Guid>
{
    private ProcessedNotificationEvent() { }
    public string Consumer { get; private set; } = string.Empty;
    public Guid MessageId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string MessageType { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }
    public int NotificationCount { get; private set; }
    public string? SkippedReason { get; private set; }

    public static ProcessedNotificationEvent Record(string consumer, Guid messageId, Guid organizationId,
        string messageType, string payloadHash, DateTimeOffset occurredAt, DateTimeOffset processedAt,
        int notificationCount, string? skippedReason)
    {
        if (string.IsNullOrWhiteSpace(consumer) || consumer.Length > 120 || messageId == Guid.Empty ||
            organizationId == Guid.Empty || string.IsNullOrWhiteSpace(messageType) || messageType.Length > 120 ||
            payloadHash is null || payloadHash.Length != 64 || !payloadHash.All(Uri.IsHexDigit) ||
            occurredAt == default || processedAt == default || notificationCount < 0 || skippedReason?.Length > 120)
            throw new DomainException("NOTIFICATION_INVALID_PROCESSED_EVENT", "La marca de procesamiento requiere identidad y huella validas.");
        return new ProcessedNotificationEvent
        {
            Id = Guid.CreateVersion7(), Consumer = consumer, MessageId = messageId, OrganizationId = organizationId,
            MessageType = messageType, PayloadHash = payloadHash, OccurredAt = occurredAt.ToUniversalTime(),
            ProcessedAt = processedAt.ToUniversalTime(), NotificationCount = notificationCount, SkippedReason = skippedReason
        };
    }
}
