using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RutaSegura.Notification.Infrastructure.Messaging.Inbound;

namespace RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;

public sealed class NotificationRabbitMqConsumer(NotificationRabbitMqConnection connection, NotificationRabbitMqOptions options,
    NotificationDeliveryCoordinator coordinator, ILogger<NotificationRabbitMqConsumer> logger) : BackgroundService
{
    private readonly TaskCompletionSource _firstSubscription = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _isConsuming;
    public bool IsConsuming => _isConsuming;
    public Task WaitUntilConsumingAsync(CancellationToken ct) => _firstSubscription.Task.WaitAsync(ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunSessionAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // No ex.Message/stack/payload: pueden contener detalles de terceros.
                logger.LogWarning("Consumidor {Queue} desconectado ({Failure}); se recuperara sin confirmar pendientes.", options.Queue, ex.GetType().Name);
            }
            finally { _isConsuming = false; }
            try { await Task.Delay(options.ReconnectDelay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task RunSessionAsync(CancellationToken stoppingToken)
    {
        var conn = await connection.GetAsync(stoppingToken);
        await using var consumeChannel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);
        await using var publishChannel = await conn.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), stoppingToken);
        await using var publisher = new NotificationFailurePublisher(options, publishChannel);
        await NotificationRabbitMqTopology.DeclareAsync(consumeChannel, options, stoppingToken);
        await consumeChannel.BasicQosAsync(0, options.PrefetchCount, false, stoppingToken);
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(consumeChannel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                // La memoria sigue siendo valida hasta terminar ESTE callback, incluidos sus awaits.
                // No se retiene ni se encola fuera del callback; el decoder limita el tamano de negocio.
                var body = delivery.Body;
                var properties = delivery.BasicProperties;
                var message = new NotificationIncomingMessage(properties.MessageId, properties.Type, properties.CorrelationId,
                    delivery.RoutingKey, properties.Headers is null ? null : new Dictionary<string, object?>(properties.Headers),
                    body, properties.ContentType, properties.Timestamp.UnixTime);
                await coordinator.HandleAsync(message, new DeliveryActions(consumeChannel, publisher, message, delivery.DeliveryTag), session.Token);
            }
            catch (Exception ex)
            {
                // Incluye ACK perdido o transferencia sin confirmacion. Cerrar la sesion hace reentregar el original.
                failed.TrySetResult(ex);
                await session.CancelAsync();
            }
        };
        await consumeChannel.BasicConsumeAsync(options.Queue, false, consumer, stoppingToken);
        _isConsuming = true;
        _firstSubscription.TrySetResult();
        logger.LogInformation("Consumiendo {Queue} con ACK manual y {RetryCount} reintentos.", options.Queue, options.RetryDelaysMilliseconds.Length);
        while (!stoppingToken.IsCancellationRequested && consumeChannel.IsOpen && publishChannel.IsOpen)
        {
            if (failed.Task.IsCompleted) throw await failed.Task;
            await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
        }
        // Dispose cierra el canal: RabbitMQ conserva y reentrega cualquier mensaje sin ACK.
    }

    private sealed class DeliveryActions(IChannel channel, NotificationFailurePublisher publisher, NotificationIncomingMessage message,
        ulong deliveryTag) : INotificationDeliveryActions
    {
        public Task AcknowledgeAsync(CancellationToken ct) => channel.BasicAckAsync(deliveryTag, false, ct).AsTask();
        public Task ForwardAsync(int? retryAttempt, string errorCode, CancellationToken ct) => publisher.ForwardAsync(message, retryAttempt, errorCode, ct);
    }
}
