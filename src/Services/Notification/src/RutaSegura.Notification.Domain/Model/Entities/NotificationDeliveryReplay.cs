using RutaSegura.BuildingBlocks.Domain;

namespace RutaSegura.Notification.Domain.Model.Entities;

/// <summary>Auditoria durable e identidad idempotente del reproceso. No guarda texto libre ni tokens.</summary>
public sealed class NotificationDeliveryReplay : Entity<Guid>
{
    private NotificationDeliveryReplay() { }
    public Guid OrganizationId { get; private set; }
    public Guid DeliveryId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string ReasonCode { get; private set; } = "";
    public DateTimeOffset RequestedAt { get; private set; }
    public static NotificationDeliveryReplay Record(Guid org, Guid delivery, Guid request, Guid actor, string reason, DateTimeOffset now)
    {
        if (org == Guid.Empty || delivery == Guid.Empty || request == Guid.Empty || actor == Guid.Empty ||
            reason is not ("ProviderRecovered" or "ConfigurationFixed" or "ManualReview"))
            throw new DomainException("DELIVERY_INVALID_REPLAY", "El reproceso requiere identidad y un motivo permitido.");
        return new() { Id = Guid.CreateVersion7(), OrganizationId = org, DeliveryId = delivery, RequestId = request,
            RequestedByUserId = actor, ReasonCode = reason, RequestedAt = now.ToUniversalTime() };
    }
}
