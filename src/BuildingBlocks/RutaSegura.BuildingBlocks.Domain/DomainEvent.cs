namespace RutaSegura.BuildingBlocks.Domain;

/// <summary>
/// Hecho de negocio que ya ocurrió dentro de un agregado.
/// La infraestructura lo traduce a evento de integración y lo publica en RabbitMQ vía Outbox.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    /// <summary>Momento de negocio en que ocurrió el hecho (puede venir del dispositivo si fue offline).</summary>
    DateTimeOffset OccurredOn { get; }
}

public abstract record DomainEvent(DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();
}
