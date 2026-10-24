using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Monolith.Administration.Infrastructure.Messaging.RabbitMq;

/// <summary>Single lazy connection per process, with automatic recovery (same setup as Trip &amp; Tracking).</summary>
public sealed class RabbitMqConnection(
    IOptions<AdministrationMessagingOptions> options,
    ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public AdministrationMessagingOptions Options => options.Value;

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
            logger.LogInformation("Administration connected to RabbitMQ {Host}:{Port}{VirtualHost}", o.HostName, o.Port,
                o.VirtualHost);
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
                logger.LogDebug(ex, "Error closing the RabbitMQ connection");
            }

            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}

/// <summary>Idempotent declarations of the exchanges and queues Administration owns.</summary>
public static class RabbitMqTopology
{
    /// <summary>
    ///     Topic exchange with an alternate exchange: while nobody is bound (e.g. Trip &amp; Tracking not deployed)
    ///     messages wait in <c>administration.events.unrouted</c> instead of being dropped.
    /// </summary>
    public static async Task DeclareEventsExchangeAsync(IChannel channel, string exchange, CancellationToken ct)
    {
        var unrouted = exchange + ".unrouted";
        await channel.ExchangeDeclareAsync(unrouted, ExchangeType.Fanout, durable: true, autoDelete: false,
            cancellationToken: ct);
        await channel.QueueDeclareAsync(unrouted, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);
        await channel.QueueBindAsync(unrouted, unrouted, routingKey: string.Empty, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["alternate-exchange"] = unrouted }, cancellationToken: ct);
    }

    /// <summary>Durable inbound queue with dead-lettering to <c>.dlq</c>, bound to Trip &amp; Tracking's exchange.</summary>
    public static async Task DeclareInboundQueueAsync(IChannel channel, AdministrationMessagingOptions o,
        CancellationToken ct)
    {
        var dlx = o.InboundQueue + ".dlx";
        var dlq = o.InboundQueue + ".dlq";
        await channel.ExchangeDeclareAsync(dlx, ExchangeType.Fanout, durable: true, autoDelete: false,
            cancellationToken: ct);
        await channel.QueueDeclareAsync(dlq, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(dlq, dlx, routingKey: string.Empty, cancellationToken: ct);
        await channel.QueueDeclareAsync(o.InboundQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = dlx }, cancellationToken: ct);

        foreach (var binding in o.EffectiveInboundBindings)
        {
            // The source exchange belongs to Trip & Tracking. It is declared with the same arguments its owner uses
            // (topic + alternate-exchange); otherwise whichever service starts second gets PRECONDITION_FAILED.
            await DeclareEventsExchangeAsync(channel, binding.Exchange, ct);
            await channel.QueueBindAsync(o.InboundQueue, binding.Exchange, binding.RoutingKey, cancellationToken: ct);
        }
    }

    /// <summary>RabbitMQ delivers text headers as byte[].</summary>
    public static string? GetHeader(IDictionary<string, object?>? headers, string key) =>
        headers is not null && headers.TryGetValue(key, out var value)
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                null => null,
                _ => value.ToString()
            }
            : null;
}
