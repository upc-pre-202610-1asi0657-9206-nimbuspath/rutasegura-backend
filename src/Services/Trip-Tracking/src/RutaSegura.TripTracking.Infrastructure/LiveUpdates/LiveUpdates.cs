using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Infrastructure.Options;

namespace RutaSegura.TripTracking.Infrastructure.LiveUpdates;

// ─────────────────────────────────────────────────────────────────────────────
// Canal en tiempo real (QA-5: posición del bus en el mapa del tutor en < 5 s).
//
//   RecordPositions ─► LivePositionPublisher ──(después del commit)──► Redis  trip:{id}:position
//                                                                 └──► ILiveUpdateBroadcaster
//                                                                        ├─ RabbitMQ exchange "trip-tracking.live"
//                                                                        │    └─► LiveUpdatesRelay (cada réplica de la API) ─► SignalR
//                                                                        └─ en proceso (sin broker, desarrollo)  ─► SignalR
//
// Es telemetría efímera: no pasa por el Outbox y un fallo nunca tumba el registro del GPS.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Cambio de estado del viaje que el mapa del tutor debe reflejar al instante (inicio, retraso, fin).</summary>
public sealed record LiveTripStatusUpdate(Guid TripId, string Status, string? Reason, DateTimeOffset OccurredAt);

/// <summary>Difunde actualizaciones en vivo a todas las réplicas de la API.</summary>
public interface ILiveUpdateBroadcaster
{
    Task BroadcastPositionAsync(LivePositionUpdate update, CancellationToken cancellationToken);

    Task BroadcastStatusAsync(LiveTripStatusUpdate update, CancellationToken cancellationToken);
}

/// <summary>
/// Destino final dentro de una réplica (lo implementa la API con el hub de SignalR).
/// Infrastructure solo conoce este puerto, no SignalR.
/// </summary>
public interface ILiveUpdateSink
{
    Task PositionAsync(LivePositionUpdate update, CancellationToken cancellationToken);

    Task StatusAsync(LiveTripStatusUpdate update, CancellationToken cancellationToken);
}

internal sealed class NullLiveUpdateSink : ILiveUpdateSink
{
    public Task PositionAsync(LivePositionUpdate update, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StatusAsync(LiveTripStatusUpdate update, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Sin broker (desarrollo / una sola réplica): entrega directa al sink local.</summary>
internal sealed class InProcessLiveUpdateBroadcaster(ILiveUpdateSink sink) : ILiveUpdateBroadcaster
{
    public Task BroadcastPositionAsync(LivePositionUpdate update, CancellationToken cancellationToken) =>
        sink.PositionAsync(update, cancellationToken);

    public Task BroadcastStatusAsync(LiveTripStatusUpdate update, CancellationToken cancellationToken) =>
        sink.StatusAsync(update, cancellationToken);
}

/// <summary>
/// Acciones que solo deben ocurrir si la transacción se confirmó (patrón "after commit").
/// Las ejecuta <see cref="Persistence.EfUnitOfWork"/>; un error se registra y no se propaga.
/// </summary>
internal sealed class PostCommitActions(ILogger<PostCommitActions> logger)
{
    private readonly List<(string Name, Func<CancellationToken, Task> Action)> _actions = [];

    public void Enqueue(string name, Func<CancellationToken, Task> action) => _actions.Add((name, action));

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var pending = _actions.ToList();
        _actions.Clear();
        foreach (var (name, action) in pending)
        {
            try
            {
                await action(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Acción posterior al commit '{Action}' falló; se omite (dato efímero)", name);
            }
        }
    }

    public void Discard() => _actions.Clear();
}

/// <summary>Adaptador de <see cref="ILivePositionPublisher"/>: Redis + difusión, ambos después del commit.</summary>
internal sealed class LivePositionPublisher(
    PostCommitActions afterCommit,
    ILastPositionCache cache,
    ILiveUpdateBroadcaster broadcaster) : ILivePositionPublisher
{
    public Task PublishAsync(LivePositionUpdate update, CancellationToken cancellationToken)
    {
        afterCommit.Enqueue("cache-last-position", ct => cache.SetAsync(update, ct));
        afterCommit.Enqueue("broadcast-position", ct => broadcaster.BroadcastPositionAsync(update, ct));
        return Task.CompletedTask;
    }
}

/// <summary>Última posición en Redis (o memoria en desarrollo) con la clave del ADR-02.2: <c>trip:{id}:position</c>.</summary>
internal sealed class DistributedLastPositionCache(
    IDistributedCache cache, IOptions<LivePositionOptions> options, ILogger<DistributedLastPositionCache> logger) : ILastPositionCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Key(Guid tripId) => $"trip:{tripId}:position";

    public Task SetAsync(LivePositionUpdate update, CancellationToken cancellationToken) =>
        cache.SetStringAsync(Key(update.TripId), JsonSerializer.Serialize(update, Json),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = options.Value.CacheTtl },
            cancellationToken);

    /// <summary>Cache-aside: si Redis no responde se devuelve null y el llamador usa MySQL.</summary>
    public async Task<LivePositionUpdate?> GetAsync(TripId tripId, CancellationToken cancellationToken)
    {
        try
        {
            var json = await cache.GetStringAsync(Key(tripId.Value), cancellationToken);
            return json is null ? null : JsonSerializer.Deserialize<LivePositionUpdate>(json, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo leer la última posición del viaje {TripId} desde la caché", tripId);
            return null;
        }
    }

    public Task RemoveAsync(TripId tripId, CancellationToken cancellationToken) =>
        cache.RemoveAsync(Key(tripId.Value), cancellationToken);
}
