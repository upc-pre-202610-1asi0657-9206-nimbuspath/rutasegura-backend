using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RutaSegura.TripTracking.Infrastructure.Options;

namespace RutaSegura.TripTracking.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Conexión única y perezosa al broker (una por proceso, como recomienda RabbitMQ).
/// Usa recuperación automática del cliente: si el broker se reinicia, conexión y topología se restablecen.
/// </summary>
internal sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqOptions Options => options.Value;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true }) return _connection;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            var o = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = o.HostName,
                Port = o.Port,
                UserName = o.UserName,
                Password = o.Password,
                VirtualHost = o.VirtualHost,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                RequestedHeartbeat = TimeSpan.FromSeconds(30),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                ClientProvidedName = o.ClientName
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            logger.LogInformation("Conectado a RabbitMQ {Host}:{Port}{VirtualHost}", o.HostName, o.Port, o.VirtualHost);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Error al cerrar la conexión con RabbitMQ");
            }

            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}

/// <summary>Declaraciones idempotentes de exchanges y colas propias del servicio.</summary>
internal static class RabbitMqTopology
{
    public const string UnroutedSuffix = ".unrouted";
    public const string DeadLetterSuffix = ".dlx";
    public const string DeadLetterQueueSuffix = ".dlq";

    /// <summary>
    /// Exchange de eventos con <c>alternate-exchange</c>: si todavía no hay consumidores enlazados
    /// (p. ej. Notification no desplegado) los mensajes quedan en <c>trip-tracking.events.unrouted</c> en vez de perderse.
    /// </summary>
    public static async Task DeclareEventsExchangeAsync(IChannel channel, string exchange, CancellationToken ct)
    {
        var unrouted = exchange + UnroutedSuffix;
        await channel.ExchangeDeclareAsync(unrouted, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(unrouted, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(unrouted, unrouted, routingKey: string.Empty, cancellationToken: ct);

        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["alternate-exchange"] = unrouted }, cancellationToken: ct);
    }

    public static Task DeclareLiveExchangeAsync(IChannel channel, string exchange, CancellationToken ct) =>
        channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);

    /// <summary>Cola de entrada durable con dead-letter: lo que no se puede procesar termina en <c>.dlq</c> para revisión.</summary>
    public static async Task DeclareInboundQueueAsync(IChannel channel, RabbitMqOptions o, CancellationToken ct)
    {
        var dlx = o.InboundQueue + DeadLetterSuffix;
        var dlq = o.InboundQueue + DeadLetterQueueSuffix;
        await channel.ExchangeDeclareAsync(dlx, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(dlq, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(dlq, dlx, routingKey: string.Empty, cancellationToken: ct);

        await channel.QueueDeclareAsync(o.InboundQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = dlx }, cancellationToken: ct);

        foreach (var binding in o.InboundBindings)
        {
            // El exchange de origen pertenece a otro servicio; declararlo (topic, durable) es idempotente
            // y permite arrancar en cualquier orden.
            await channel.ExchangeDeclareAsync(binding.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
            await channel.QueueBindAsync(o.InboundQueue, binding.Exchange, binding.RoutingKey, cancellationToken: ct);
        }
    }
}

/// <summary>Cabeceras del sobre de integración (BuildingBlocks.Messaging) en mensajes AMQP.</summary>
internal static class MessageHeaders
{
    public const string MessageType = "message-type";
    public const string SchemaVersion = "schema-version";
    public const string OccurredAt = "occurred-at";
    public const string CorrelationId = "correlation-id";

    /// <summary>RabbitMQ entrega las cabeceras de texto como byte[]; esto las normaliza.</summary>
    public static string? GetString(IDictionary<string, object?>? headers, string key) =>
        headers is not null && headers.TryGetValue(key, out var value)
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                null => null,
                _ => value.ToString()
            }
            : null;
}
