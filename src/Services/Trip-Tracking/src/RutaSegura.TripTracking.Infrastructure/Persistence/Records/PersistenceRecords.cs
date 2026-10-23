namespace RutaSegura.TripTracking.Infrastructure.Persistence.Records;

// Modelos de persistencia que NO son agregados del dominio: telemetría, Outbox/Inbox y réplicas.
// Viven solo en Infrastructure; la capa Application los ve a través de sus puertos.

/// <summary>Fila append-only de la tabla <c>track_points</c> (AC-03: no reescribe el viaje por cada ping).</summary>
internal sealed class TrackPointRecord
{
    public long Id { get; set; }
    public Guid TripId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public double AccuracyMeters { get; set; }
    public double? SpeedKmh { get; set; }
    public double? HeadingDegrees { get; set; }
}

/// <summary>
/// Transactional Outbox (PT-04): el evento se guarda en la MISMA transacción que el cambio del viaje.
/// El <c>OutboxProcessor</c> lo publica después en RabbitMQ y marca <see cref="ProcessedAt"/>.
/// </summary>
internal sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public Guid AggregateId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Idempotent Consumer (PT-04): un MessageId recibido se procesa una sola vez.</summary>
internal sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>Réplica de una ruta de Administration. Se guarda el snapshot completo en JSON.</summary>
internal sealed class RouteSnapshotRecord
{
    public Guid RouteId { get; set; }
    public Guid? OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Snapshot { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Réplica del vínculo tutor → alumno (Roster / IAM).</summary>
internal sealed class GuardianStudentLink
{
    public Guid GuardianId { get; set; }
    public Guid StudentId { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}
