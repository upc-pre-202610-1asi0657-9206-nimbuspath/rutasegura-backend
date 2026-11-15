using RutaSegura.BuildingBlocks.Domain;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Domain.Model.Entities;

/// <summary>Trabajo durable pendiente de expandir a entregas por canal; no acredita envio ni lectura.</summary>
public sealed class NotificationDispatchJob : Entity<Guid>
{
    private NotificationDispatchJob() { }
    public Guid OrganizationId { get; private set; }
    public Guid NotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpandedAt { get; private set; }

    public static NotificationDispatchJob Schedule(NotificationEntity notification) => new()
    {
        Id = Guid.CreateVersion7(), OrganizationId = notification.OrganizationId,
        NotificationId = notification.Id, CreatedAt = notification.CreatedAt
    };

    public bool MarkExpanded(DateTimeOffset now)
    {
        if (ExpandedAt is not null) return false;
        if (now < CreatedAt)
            throw new DomainException("NOTIFICATION_INVALID_DISPATCH_TIME", "La expansion no puede preceder a la creacion.");
        ExpandedAt = now.ToUniversalTime();
        return true;
    }
}
