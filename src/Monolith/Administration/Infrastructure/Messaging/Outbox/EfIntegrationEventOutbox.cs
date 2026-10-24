using System.Diagnostics;
using System.Text.Json;
using Monolith.Administration.Application.IntegrationEvents;
using Monolith.Administration.Application.Internal.OutboundServices;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;

namespace Monolith.Administration.Infrastructure.Messaging.Outbox;

/// <summary>
///     Outbox adapter: adds the message to the <see cref="AppDbContext" /> of the request, so it is committed by the
///     same <c>IUnitOfWork.CompleteAsync</c> that saves the aggregate (no message without change, no change without message).
/// </summary>
public class EfIntegrationEventOutbox(AppDbContext context, TimeProvider clock) : IIntegrationEventOutbox
{
    public static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    // Keeps the order in which one unit of work enqueued messages that share the same instant.
    private int _sequence;

    public Task EnqueueAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        context.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = integrationEvent.EventId,
            MessageType = integrationEvent.MessageType,
            SchemaVersion = integrationEvent.SchemaVersion,
            Payload = JsonSerializer.Serialize(integrationEvent.Payload, integrationEvent.Payload.GetType(),
                PayloadJson),
            CorrelationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"),
            OccurredAt = integrationEvent.OccurredAt,
            CreatedAt = clock.GetUtcNow().AddMicroseconds(_sequence++)
        });
        return Task.CompletedTask;
    }
}
