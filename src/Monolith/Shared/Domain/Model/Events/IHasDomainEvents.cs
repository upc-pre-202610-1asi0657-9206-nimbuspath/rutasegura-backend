namespace Monolith.Shared.Domain.Model.Events;

/// <summary>
///     Implemented by aggregates that record domain events while their state changes.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>Events recorded since the aggregate was loaded or since the last clear.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Discards the recorded events once the application layer has dispatched them.</summary>
    void ClearDomainEvents();
}
