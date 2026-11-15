using System.Diagnostics;
using System.Text.Json;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Stakeholder.Application.IntegrationEvents;
using Monolith.Stakeholder.Application.Internal.OutboundServices;

namespace Monolith.Stakeholder.Infrastructure.Messaging;

/// <summary>Adds the message to the request's DbContext: it is committed together with the change.</summary>
public class EfStakeholderEventOutbox(AppDbContext context, TimeProvider clock) : IStakeholderEventOutbox
{
    public static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);
    private int _sequence;

    public Task EnqueueAsync(StakeholderIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        context.Set<StakeholderOutboxMessage>().Add(new StakeholderOutboxMessage
        {
            Id = integrationEvent.EventId,
            MessageType = integrationEvent.MessageType,
            SchemaVersion = integrationEvent.SchemaVersion,
            Payload = JsonSerializer.Serialize(integrationEvent.Payload, integrationEvent.Payload.GetType(), PayloadJson),
            CorrelationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"),
            OccurredAt = integrationEvent.OccurredAt,
            CreatedAt = clock.GetUtcNow().AddMicroseconds(_sequence++)
        });
        return Task.CompletedTask;
    }
}
