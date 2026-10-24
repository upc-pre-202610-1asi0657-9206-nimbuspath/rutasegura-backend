using Monolith.Administration.Application.IntegrationEvents;

namespace Monolith.Administration.Application.Internal.OutboundServices;

/// <summary>
///     Outbox port (PT-04). The infrastructure adapter stores the event in the same database transaction as the
///     business change (it is written by <c>IUnitOfWork.CompleteAsync</c>) and a background publisher sends it
///     to RabbitMQ afterwards.
/// </summary>
public interface IIntegrationEventOutbox
{
    Task EnqueueAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken);
}
