using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Infrastructure.Messaging.RabbitMq;
using RutaSegura.TripTracking.Infrastructure.Persistence;
using RutaSegura.TripTracking.Infrastructure.Persistence.Records;

namespace RutaSegura.TripTracking.Infrastructure.Messaging.Inbound;

public enum InboundOutcome
{
    Processed,
    Duplicate,
    Ignored,
    Rejected,
    Retry
}

/// <summary>
/// Idempotent Consumer (PT-04) de la cola <c>trip-tracking.inbound</c>.
/// Por mensaje: (1) si su MessageId ya está en <c>inbox_messages</c> se confirma sin repetir efectos;
/// (2) se traduce a un comando y se despacha; el TransactionCommandDecorator guarda réplica + Inbox juntos;
/// (3) ACK solo después del commit. Contenido inválido → DLQ; error transitorio → un reintento y luego DLQ.
/// </summary>
internal sealed class InboundEventsConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<InboundEventsConsumer> logger) : BackgroundService
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
                logger.LogInformation("Consumiendo {Queue} ({Bindings} suscripciones)", options.InboundQueue, options.InboundBindings.Count);
                delay = TimeSpan.FromSeconds(2);

                // El cliente recupera la conexión solo; se vuelve a montar el consumidor si el canal muere del todo.
                while (!stoppingToken.IsCancellationRequested && channel.IsOpen)
                    await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Consumidor de entrada sin conexión con RabbitMQ; reintento en {Delay}", delay);
                await Task.Delay(delay, clock, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
            finally
            {
                if (channel is not null)
                {
                    try { await channel.DisposeAsync(); } catch { /* el canal ya estaba cerrado */ }
                }
            }
        }
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var props = delivery.BasicProperties;
        var messageType = props.Type ?? MessageHeaders.GetString(props.Headers, MessageHeaders.MessageType) ?? delivery.RoutingKey;
        var outcome = await HandleAsync(props.MessageId, messageType, delivery.Body, delivery.Redelivered, stoppingToken);

        switch (outcome)
        {
            case InboundOutcome.Processed or InboundOutcome.Duplicate or InboundOutcome.Ignored:
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                break;
            case InboundOutcome.Retry:
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, stoppingToken);
                break;
            default:
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken); // → DLQ
                break;
        }
    }

    /// <summary>Lógica de consumo sin dependencias de AMQP (se prueba directamente).</summary>
    internal async Task<InboundOutcome> HandleAsync(string? messageIdText, string messageType, ReadOnlyMemory<byte> body,
        bool redelivered, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(messageIdText, out var messageId) || messageId == Guid.Empty)
        {
            logger.LogWarning("Mensaje {Type} sin MessageId válido: se envía a la DLQ", messageType);
            return InboundOutcome.Rejected;
        }

        ICommand<Unit>? command;
        try
        {
            command = InboundMessageTranslator.Translate(messageType, body.Span);
        }
        catch (InvalidInboundMessageException ex)
        {
            logger.LogWarning(ex, "Mensaje {MessageId} ({Type}) inválido: se envía a la DLQ", messageId, messageType);
            return InboundOutcome.Rejected;
        }

        if (command is null)
        {
            logger.LogDebug("Mensaje {Type} no aplica a Trip & Tracking: se descarta", messageType);
            return InboundOutcome.Ignored;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TripTrackingDbContext>();
            if (await db.InboxMessages.AsNoTracking().AnyAsync(m => m.MessageId == messageId, cancellationToken))
                return InboundOutcome.Duplicate;

            db.InboxMessages.Add(new InboxMessage { MessageId = messageId, MessageType = messageType, ProcessedAt = clock.GetUtcNow() });

            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var result = await dispatcher.SendAsync(command, cancellationToken);
            if (result.IsSuccess) return InboundOutcome.Processed;

            logger.LogWarning("Mensaje {MessageId} ({Type}) rechazado: {Code} {Message}",
                messageId, messageType, result.FirstError.Code, result.FirstError.Message);
            return InboundOutcome.Rejected;
        }
        catch (PersistenceConflictException ex) when (ex.Code == PersistenceConflictException.DuplicateCode)
        {
            return InboundOutcome.Duplicate; // otra réplica lo procesó al mismo tiempo
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error procesando {MessageId} ({Type}); {Action}", messageId, messageType,
                redelivered ? "segundo intento fallido, va a la DLQ" : "se reintentará una vez");
            return redelivered ? InboundOutcome.Rejected : InboundOutcome.Retry;
        }
    }
}
