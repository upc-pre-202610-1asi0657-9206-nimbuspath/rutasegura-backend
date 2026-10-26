using RutaSegura.BuildingBlocks.Domain;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;
using RutaSegura.Notification.Domain.Model.ValueObjects;

namespace RutaSegura.Notification.Domain.Model.Entities;

/// <summary>Una entrega por canal y destino. Push usa un identificador de suscripcion, nunca el token en el dominio.</summary>
public sealed class NotificationDelivery : Entity<Guid>
{
    private NotificationDelivery() { }

    public Guid NotificationId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public Guid DestinationId { get; private set; }
    public DeliveryStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int CycleAttemptCount { get; private set; }
    public int ReplayCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? ProviderMessageId { get; private set; }

    public static NotificationDelivery Schedule(NotificationEntity notification, NotificationChannel channel,
        Guid destinationId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (!Enum.IsDefined(channel) || destinationId == Guid.Empty || now < notification.CreatedAt)
            throw new DomainException("DELIVERY_INVALID_DATA", "La entrega requiere canal, destino y fecha validos.");
        return new NotificationDelivery
        {
            Id = Guid.CreateVersion7(), NotificationId = notification.Id, OrganizationId = notification.OrganizationId,
            Channel = channel, DestinationId = destinationId, Status = DeliveryStatus.Pending, CreatedAt = now.ToUniversalTime()
        };
    }

    public void AcceptByProvider(string providerMessageId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(providerMessageId))
            throw new DomainException("DELIVERY_INVALID_RECEIPT", "El proveedor debe devolver una referencia de aceptacion.");
        RecordAttempt(now);
        ProviderMessageId = providerMessageId;
        LastErrorCode = null;
        NextAttemptAt = null;
        Status = DeliveryStatus.ProviderAccepted;
    }

    public void Retry(string errorCode, DateTimeOffset nextAttemptAt, DateTimeOffset now)
    {
        ValidateError(errorCode);
        if (nextAttemptAt <= now)
            throw new DomainException("DELIVERY_INVALID_RETRY_TIME", "El reintento debe programarse para un instante posterior.");
        RecordAttempt(now);
        LastErrorCode = errorCode;
        NextAttemptAt = nextAttemptAt.ToUniversalTime();
        Status = DeliveryStatus.RetryScheduled;
    }

    public void Fail(string errorCode, DateTimeOffset now)
    {
        ValidateError(errorCode);
        RecordAttempt(now);
        LastErrorCode = errorCode;
        NextAttemptAt = null;
        Status = DeliveryStatus.Failed;
    }

    public void Skip(string reason)
    {
        ValidateError(reason);
        EnsurePending();
        LastErrorCode = reason;
        NextAttemptAt = null;
        Status = DeliveryStatus.Skipped;
    }

    public void Replay(DateTimeOffset now, int maxReplays)
    {
        if (Status != DeliveryStatus.Failed || ReplayCount >= maxReplays || maxReplays is < 1 or > 10)
            throw new DomainException("DELIVERY_REPLAY_NOT_ALLOWED", "Solo se reprocesan entregas fallidas dentro de su presupuesto.");
        if (now < CreatedAt || now < LastAttemptAt) throw new DomainException("DELIVERY_INVALID_REPLAY_TIME", "Instante de reproceso invalido.");
        ReplayCount++;
        CycleAttemptCount = 0;
        Status = DeliveryStatus.RetryScheduled;
        NextAttemptAt = now;
        LastErrorCode = null;
    }

    private void RecordAttempt(DateTimeOffset now)
    {
        EnsurePending();
        if (now < CreatedAt || (LastAttemptAt is not null && now < LastAttemptAt) ||
            (NextAttemptAt is not null && now < NextAttemptAt))
            throw new DomainException("DELIVERY_INVALID_ATTEMPT_TIME", "El intento debe respetar el orden temporal y el reintento programado.");
        AttemptCount++;
        CycleAttemptCount++;
        LastAttemptAt = now.ToUniversalTime();
    }

    private void EnsurePending()
    {
        if (Status is not (DeliveryStatus.Pending or DeliveryStatus.RetryScheduled))
            throw new DomainException("DELIVERY_ALREADY_FINISHED", "Una entrega finalizada no admite nuevos intentos.");
    }

    private static void ValidateError(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 120)
            throw new DomainException("DELIVERY_INVALID_ERROR_CODE", "Se requiere un codigo estable sin detalles sensibles del proveedor.");
    }
}
