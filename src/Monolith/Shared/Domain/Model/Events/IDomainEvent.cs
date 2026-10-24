namespace Monolith.Shared.Domain.Model.Events;

/// <summary>
///     Marks a fact that happened inside an aggregate and that other parts of the system may react to.
/// </summary>
/// <remarks>
///     Domain events stay inside the monolith. The application layer of each bounded context decides
///     which of them are translated into integration events (published language) for other services.
/// </remarks>
public interface IDomainEvent
{
    /// <summary>Unique identifier of the event; reused as message id when it becomes an integration event.</summary>
    Guid EventId { get; }
}
