using Monolith.Stakeholder.Application.IntegrationEvents;

namespace Monolith.Stakeholder.Application.Internal.OutboundServices;

/// <summary>
///     Transactional outbox port (PT-04): the event is stored by the same commit as the change and published to
///     <c>stakeholder.events</c> afterwards.
/// </summary>
public interface IStakeholderEventOutbox
{
    Task EnqueueAsync(StakeholderIntegrationEvent integrationEvent, CancellationToken cancellationToken);
}
