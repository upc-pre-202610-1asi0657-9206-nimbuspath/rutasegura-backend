using Monolith.Administration.Infrastructure.Messaging.RabbitMq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Monolith.Administration.Infrastructure.Messaging.Inbound;

/// <summary>
///     Consumes <c>administration.inbound</c> (IncidentReported, SosRaised, SosResolved, TripCompleted from Trip &amp;
///     Tracking). ACK only after the commit; invalid content → DLQ; transient error → one retry, then DLQ.
/// </summary>
public sealed class TripTrackingEventsConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<TripTrackingEventsConsumer> logger) : BackgroundService
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
                var options = connection.Options;
                await RabbitMqTopology.DeclareInboundQueueAsync(channel, options, stoppingToken);
                await channel.BasicQosAsync(0, options.PrefetchCount, global: false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, delivery, stoppingToken);
                await channel.BasicConsumeAsync(options.InboundQueue, autoAck: false, consumer, stoppingToken);
                logger.LogInformation("Administration consuming {Queue}", options.InboundQueue);
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
                logger.LogWarning(ex, "Administration consumer without RabbitMQ; retrying in {Delay}", delay);
                await Task.Delay(delay, clock, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
            finally
            {
                if (channel is not null)
                    try
                    {
                        await channel.DisposeAsync();
                    }
                    catch
                    {
                        // the channel was already closed
                    }
            }
        }
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var props = delivery.BasicProperties;
        var messageType = props.Type ?? RabbitMqTopology.GetHeader(props.Headers, "message-type") ?? delivery.RoutingKey;

        await using var scope = scopes.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<InboundMessageProcessor>();
        var outcome = await processor.HandleAsync(props.MessageId, messageType, delivery.Body, delivery.Redelivered, ct);

        switch (outcome)
        {
            case InboundOutcome.Processed or InboundOutcome.Duplicate or InboundOutcome.Ignored:
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
                break;
            case InboundOutcome.Retry:
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
                break;
            default:
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
                break;
        }
    }
}
