namespace Monolith.Administration.Infrastructure.Messaging.Outbox;

/// <summary>Sends one outbox message to the broker; completes only when the broker confirmed it.</summary>
public interface IOutboxMessagePublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
