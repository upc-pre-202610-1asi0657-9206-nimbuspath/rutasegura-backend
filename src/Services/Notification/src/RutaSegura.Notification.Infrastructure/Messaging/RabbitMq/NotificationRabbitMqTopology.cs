using RabbitMQ.Client;

namespace RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;

public static class NotificationRabbitMqTopology
{
    public static readonly string[] RoutingKeys =
    [
        "trip-tracking.student-boarded", "trip-tracking.student-alighted", "trip-tracking.vehicle-approaching-stop",
        "trip-tracking.incident-reported", "trip-tracking.sos-raised", "trip-tracking.trip-completed"
    ];

    public static async Task DeclareAsync(IChannel channel, NotificationRabbitMqOptions options, CancellationToken ct)
    {
        // Misma declaracion que el propietario Trip & Tracking: evita PRECONDITION_FAILED segun orden de arranque.
        var unrouted = options.EventsExchange + ".unrouted";
        await channel.ExchangeDeclareAsync(unrouted, ExchangeType.Fanout, true, false, cancellationToken: ct);
        await channel.QueueDeclareAsync(unrouted, true, false, false, cancellationToken: ct);
        await channel.QueueBindAsync(unrouted, unrouted, string.Empty, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(options.EventsExchange, ExchangeType.Topic, true, false,
            new Dictionary<string, object?> { ["alternate-exchange"] = unrouted }, cancellationToken: ct);

        await channel.ExchangeDeclareAsync(options.ErrorExchange, ExchangeType.Direct, true, false, cancellationToken: ct);
        await channel.QueueDeclareAsync(options.ErrorQueue, true, false, false, Quorum(), cancellationToken: ct);
        await channel.QueueBindAsync(options.ErrorQueue, options.ErrorExchange, options.Queue, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(options.ReturnExchange, ExchangeType.Direct, true, false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(options.RetryExchange, ExchangeType.Direct, true, false, cancellationToken: ct);

        var inbox = Quorum();
        inbox["x-dead-letter-exchange"] = options.ErrorExchange;
        inbox["x-dead-letter-routing-key"] = options.Queue;
        inbox["x-dead-letter-strategy"] = "at-least-once";
        inbox["x-delivery-limit"] = -1; // Los intentos de negocio se cuentan explicitamente, no por cortes de conexion.
        await channel.QueueDeclareAsync(options.Queue, true, false, false, inbox, cancellationToken: ct);
        await channel.QueueBindAsync(options.Queue, options.ReturnExchange, options.Queue, cancellationToken: ct);
        foreach (var key in RoutingKeys)
            await channel.QueueBindAsync(options.Queue, options.EventsExchange, key, cancellationToken: ct);

        for (var attempt = 1; attempt <= options.RetryDelaysMilliseconds.Length; attempt++)
        {
            var retry = Quorum();
            retry["x-message-ttl"] = options.RetryDelaysMilliseconds[attempt - 1];
            retry["x-dead-letter-exchange"] = options.ReturnExchange;
            retry["x-dead-letter-routing-key"] = options.Queue;
            retry["x-dead-letter-strategy"] = "at-least-once";
            await channel.QueueDeclareAsync(options.RetryQueue(attempt), true, false, false, retry, cancellationToken: ct);
            await channel.QueueBindAsync(options.RetryQueue(attempt), options.RetryExchange, attempt.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken: ct);
        }
    }

    private static Dictionary<string, object?> Quorum() => new()
    {
        ["x-queue-type"] = "quorum", ["x-overflow"] = "reject-publish"
    };
}
