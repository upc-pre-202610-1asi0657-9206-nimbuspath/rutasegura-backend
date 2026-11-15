using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Infrastructure.LiveUpdates;

namespace RutaSegura.TripTracking.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Cada réplica de la API escucha el exchange <c>trip-tracking.live</c> con una cola exclusiva y temporal
/// y entrega lo recibido a su hub de SignalR. Así un tutor conectado a la réplica B ve el ping que llegó a la
/// réplica A, sin backplane adicional (el broker hace de bus entre réplicas).
/// </summary>
internal sealed class LiveUpdatesRelay(
    RabbitMqConnection connection,
    ILiveUpdateSink sink,
    TimeProvider clock,
    ILogger<LiveUpdatesRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!stoppingToken.IsCancellationRequested)
        {
            IChannel? channel = null;
            try
            {
                var conn = await connection.GetAsync(stoppingToken);
                channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);
                var exchange = connection.Options.LiveExchange;
                await RabbitMqTopology.DeclareLiveExchangeAsync(channel, exchange, stoppingToken);

                var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true,
                    cancellationToken: stoppingToken);
                await channel.QueueBindAsync(queue.QueueName, exchange, "#", cancellationToken: stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) => DeliverAsync(delivery, stoppingToken);
                await channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer, stoppingToken);
                logger.LogInformation("Relay en vivo escuchando {Exchange} en {Queue}", exchange, queue.QueueName);
                delay = TimeSpan.FromSeconds(2);

                while (!stoppingToken.IsCancellationRequested && channel.IsOpen)
                    await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Relay en vivo sin conexión con RabbitMQ; reintento en {Delay}", delay);
                await Task.Delay(delay, clock, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
            finally
            {
                if (channel is not null)
                {
                    try { await channel.DisposeAsync(); } catch { /* ya cerrado */ }
                }
            }
        }
    }

    private async Task DeliverAsync(BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        try
        {
            switch (delivery.BasicProperties.Type)
            {
                case RabbitMqLiveUpdateBroadcaster.PositionType:
                    var position = JsonSerializer.Deserialize<LivePositionUpdate>(delivery.Body.Span, AmqpJson.Options);
                    if (position is not null) await sink.PositionAsync(position, cancellationToken);
                    break;
                case RabbitMqLiveUpdateBroadcaster.StatusType:
                    var status = JsonSerializer.Deserialize<LiveTripStatusUpdate>(delivery.Body.Span, AmqpJson.Options);
                    if (status is not null) await sink.StatusAsync(status, cancellationToken);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo entregar la actualización en vivo {RoutingKey}", delivery.RoutingKey);
        }
    }
}
