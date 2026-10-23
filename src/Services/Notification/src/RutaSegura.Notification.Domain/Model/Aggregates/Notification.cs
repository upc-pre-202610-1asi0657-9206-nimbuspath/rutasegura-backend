using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.Notification.Domain.Model.ValueObjects;

namespace RutaSegura.Notification.Domain.Model.Aggregates;

/// <summary>Bandeja durable. Leer un aviso no confirma ni cancela su entrega por un proveedor.</summary>
public sealed class Notification : AggregateRoot<Guid>
{
    private Notification() { }

    public Guid OrganizationId { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public Guid SourceMessageId { get; private set; }
    public string SourceMessageType { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public Guid? TripId { get; private set; }
    public Guid? StudentId { get; private set; }
    public Guid? AnnouncementId { get; private set; }
    public NotificationCategory Category { get; private set; }
    public NotificationPriority Priority => Category switch
    {
        NotificationCategory.Sos => NotificationPriority.Critical,
        NotificationCategory.Incident => NotificationPriority.High,
        _ => NotificationPriority.Normal
    };
    public NotificationContent Content { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    // SubjectId no nulo evita que MySQL admita duplicados en indices UNIQUE con columnas NULL.
    public NotificationKey Key => new(OrganizationId, SourceMessageId, SourceMessageType, RecipientUserId, StudentId ?? Guid.Empty);

    public static Notification Create(Guid organizationId, Guid recipientUserId, Guid sourceMessageId,
        string sourceMessageType, string correlationId, NotificationCategory category, NotificationContent content,
        DateTimeOffset occurredAt, DateTimeOffset createdAt, Guid? tripId = null, Guid? studentId = null,
        Guid? announcementId = null, DateTimeOffset? expiresAt = null)
    {
        if (organizationId == Guid.Empty || recipientUserId == Guid.Empty || sourceMessageId == Guid.Empty ||
            tripId == Guid.Empty || studentId == Guid.Empty || announcementId == Guid.Empty ||
            string.IsNullOrWhiteSpace(sourceMessageType) || sourceMessageType.Length > 120 ||
            string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 200 || !Enum.IsDefined(category) ||
            content is null || occurredAt == default || createdAt == default ||
            (expiresAt is not null && expiresAt <= occurredAt))
            throw new DomainException("NOTIFICATION_INVALID_DATA", "El aviso requiere origen, destinatario, organizacion y fechas validas.");

        return new Notification
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId, RecipientUserId = recipientUserId,
            SourceMessageId = sourceMessageId, SourceMessageType = sourceMessageType, Category = category,
            CorrelationId = correlationId,
            Content = content, OccurredAt = occurredAt.ToUniversalTime(), CreatedAt = createdAt.ToUniversalTime(),
            TripId = tripId, StudentId = studentId, AnnouncementId = announcementId, ExpiresAt = expiresAt?.ToUniversalTime()
        };
    }

    public bool MarkAsRead(DateTimeOffset now)
    {
        if (ReadAt is not null) return false;
        if (now < CreatedAt)
            throw new DomainException("NOTIFICATION_INVALID_READ_TIME", "La lectura no puede preceder a la creacion.");
        ReadAt = now.ToUniversalTime();
        return true;
    }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is not null && now >= ExpiresAt;
}
