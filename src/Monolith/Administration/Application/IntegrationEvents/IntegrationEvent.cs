namespace Monolith.Administration.Application.IntegrationEvents;

/// <summary>
///     Integration event ready to be stored in the outbox and published to RabbitMQ (exchange
///     <c>administration.events</c>, routing key = <paramref name="MessageType" />).
/// </summary>
/// <param name="EventId">Message id; consumers use it for idempotency (Inbox).</param>
/// <param name="MessageType">E.g. <c>administration.route-published</c>.</param>
/// <param name="SchemaVersion">Contract version; renaming or removing fields requires a new version.</param>
/// <param name="OccurredAt">When the fact happened.</param>
/// <param name="Payload">One of the <c>*V1</c> contracts, serialized as camelCase JSON.</param>
public sealed record IntegrationEvent(
    Guid EventId,
    string MessageType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    object Payload);
