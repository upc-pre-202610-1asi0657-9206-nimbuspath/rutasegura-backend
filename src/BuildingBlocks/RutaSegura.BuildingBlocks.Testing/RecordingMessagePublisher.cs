using System.Collections.Concurrent;
using RutaSegura.BuildingBlocks.Messaging;

namespace RutaSegura.BuildingBlocks.Testing;

/// <summary>Captura mensajes para pruebas; no sustituye un broker en pruebas de integración.</summary>
public sealed class RecordingMessagePublisher : IMessagePublisher
{
    private readonly ConcurrentQueue<IIntegrationMessage> _messages = new();

    public IReadOnlyList<IIntegrationMessage> Messages => _messages.ToArray();

    public Task PublishAsync<TPayload>(IntegrationMessage<TPayload> message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(message);
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }
}
