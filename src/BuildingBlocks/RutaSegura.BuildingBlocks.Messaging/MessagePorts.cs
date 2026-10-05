namespace RutaSegura.BuildingBlocks.Messaging;

/// <summary>
/// Puerto implementado por Infrastructure. El adaptador debe confirmar aceptación y
/// enrutamiento acordado o fallar; no implica exactamente una entrega ni confirma el negocio.
/// </summary>
public interface IMessagePublisher
{
    Task PublishAsync<TPayload>(IntegrationMessage<TPayload> message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// El adaptador consumidor controla Inbox, transacción y ACK después de este handler.
/// El contrato no expone canales, delivery tags ni tipos de RabbitMQ a Application.
/// </summary>
public interface IMessageHandler<TPayload>
{
    Task HandleAsync(IntegrationMessage<TPayload> message, CancellationToken cancellationToken);
}
