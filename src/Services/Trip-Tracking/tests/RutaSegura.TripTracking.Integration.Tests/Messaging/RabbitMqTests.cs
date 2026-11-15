using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RutaSegura.BuildingBlocks.Messaging;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Infrastructure.LiveUpdates;
using RutaSegura.TripTracking.Infrastructure.Messaging.RabbitMq;
using RutaSegura.TripTracking.Infrastructure.Options;

namespace RutaSegura.TripTracking.Integration.Tests.Messaging;

/// <summary>
/// Adaptadores RabbitMQ contra un broker real (variable TRIPTRACKING_IT_RABBITMQ, por defecto localhost).
/// Cada prueba usa exchanges propios para no interferir con otros servicios del broker.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RabbitMqTests : IAsyncLifetime
{
    private readonly RabbitMqOptions _options;
    private RabbitMqConnection? _connection;
    private bool _available;

    public RabbitMqTests()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _options = new RabbitMqOptions
        {
            Enabled = true,
            HostName = Environment.GetEnvironmentVariable("TRIPTRACKING_IT_RABBITMQ") ?? "localhost",
            EventsExchange = $"it-{suffix}.events",
            LiveExchange = $"it-{suffix}.live",
            InboundQueue = $"it-{suffix}.inbound"
        };
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connection = new RabbitMqConnection(Options.Create(_options), NullLogger<RabbitMqConnection>.Instance);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _connection.GetAsync(timeout.Token);
            _available = true;
        }
        catch (Exception)
        {
            _available = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_available && _connection is not null)
        {
            try
            {
                var conn = await _connection.GetAsync(CancellationToken.None);
                await using var channel = await conn.CreateChannelAsync();
                await channel.ExchangeDeleteAsync(_options.EventsExchange);
                await channel.ExchangeDeleteAsync(_options.EventsExchange + RabbitMqTopology.UnroutedSuffix);
                await channel.QueueDeleteAsync(_options.EventsExchange + RabbitMqTopology.UnroutedSuffix);
                await channel.ExchangeDeleteAsync(_options.LiveExchange);
            }
            catch (Exception)
            {
                // limpieza best-effort
            }
        }

        if (_connection is not null) await _connection.DisposeAsync();
    }

    private void RequireBroker() => Assert.SkipUnless(_available, "RabbitMQ no disponible (define TRIPTRACKING_IT_RABBITMQ o levanta el docker-compose).");

    [Fact]
    public async Task EventsPublisher_WaitsForBrokerConfirmAndKeepsTheEnvelope()
    {
        RequireBroker();
        await using var publisher = new RabbitMqMessagePublisher(_connection!, NullLogger<RabbitMqMessagePublisher>.Instance);
        var conn = await _connection!.GetAsync(Ct);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: Ct);
        await RabbitMqTopology.DeclareEventsExchangeAsync(channel, _options.EventsExchange, Ct);
        var queue = await channel.QueueDeclareAsync(string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: Ct);
        await channel.QueueBindAsync(queue.QueueName, _options.EventsExchange, "trip-tracking.#", cancellationToken: Ct);

        var messageId = Guid.NewGuid();
        await publisher.PublishAsync(new IntegrationMessage<object>(messageId, "trip-tracking.sos-raised", 1,
            DateTimeOffset.UtcNow, "corr-1", new { tripId = Guid.Empty, studentsOnBoard = 9 }), Ct);

        var delivered = await channel.BasicGetAsync(queue.QueueName, autoAck: true, Ct);
        delivered.ShouldNotBeNull();
        delivered.BasicProperties.MessageId.ShouldBe(messageId.ToString());
        delivered.BasicProperties.Type.ShouldBe("trip-tracking.sos-raised");
        delivered.BasicProperties.Persistent.ShouldBeTrue();
        JsonDocument.Parse(delivered.Body).RootElement.GetProperty("studentsOnBoard").GetInt32().ShouldBe(9);
    }

    [Fact]
    public async Task EventWithoutSubscribers_IsKeptInTheUnroutedQueueInsteadOfLost()
    {
        RequireBroker();
        await using var publisher = new RabbitMqMessagePublisher(_connection!, NullLogger<RabbitMqMessagePublisher>.Instance);

        await publisher.PublishAsync(new IntegrationMessage<object>(Guid.NewGuid(), "trip-tracking.trip-started", 1,
            DateTimeOffset.UtcNow, "corr-2", new { ok = true }), Ct);

        var conn = await _connection!.GetAsync(Ct);
        await using var channel = await conn.CreateChannelAsync(cancellationToken: Ct);
        var unrouted = await channel.BasicGetAsync(_options.EventsExchange + RabbitMqTopology.UnroutedSuffix, autoAck: true, Ct);
        unrouted.ShouldNotBeNull().BasicProperties.Type.ShouldBe("trip-tracking.trip-started");
    }

    [Fact]
    public async Task LiveUpdates_TravelThroughTheBrokerToTheSinkOfEveryReplica()
    {
        RequireBroker();
        var sink = new RecordingSink();
        var relay = new LiveUpdatesRelay(_connection!, sink, TimeProvider.System, NullLogger<LiveUpdatesRelay>.Instance);
        await relay.StartAsync(Ct);
        try
        {
            await using var broadcaster = new RabbitMqLiveUpdateBroadcaster(_connection!, NullLogger<RabbitMqLiveUpdateBroadcaster>.Instance);
            var update = new LivePositionUpdate(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "InProgress",
                -12.1, -77.0, DateTimeOffset.UtcNow, 20, 90, null, null, null, 4);

            // El relay declara su cola temporal en segundo plano: se reintenta hasta que la difusión llegue.
            for (var i = 0; i < 50 && sink.Positions.IsEmpty; i++)
            {
                await broadcaster.BroadcastPositionAsync(update, Ct);
                await Task.Delay(100, Ct);
            }

            sink.Positions.ShouldNotBeEmpty();
            sink.Positions.First().TripId.ShouldBe(update.TripId);
        }
        finally
        {
            await relay.StopAsync(CancellationToken.None);
        }
    }

    private sealed class RecordingSink : ILiveUpdateSink
    {
        public ConcurrentQueue<LivePositionUpdate> Positions { get; } = new();

        public Task PositionAsync(LivePositionUpdate update, CancellationToken cancellationToken)
        {
            Positions.Enqueue(update);
            return Task.CompletedTask;
        }

        public Task StatusAsync(LiveTripStatusUpdate update, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
