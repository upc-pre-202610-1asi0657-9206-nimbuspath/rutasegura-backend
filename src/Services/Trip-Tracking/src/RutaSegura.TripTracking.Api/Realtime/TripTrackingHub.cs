using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.TripTracking.Api.Security;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Trips.Queries;
using RutaSegura.TripTracking.Infrastructure.LiveUpdates;

namespace RutaSegura.TripTracking.Api.Realtime;

/// <summary>Métodos que el servidor invoca en el cliente (app del tutor / panel de la empresa).</summary>
public interface ITripTrackingClient
{
    /// <summary>Nueva posición del bus (sin datos de alumnos, AC-01).</summary>
    Task PositionUpdated(LivePositionUpdate update);

    /// <summary>El viaje inició, se retrasó, terminó o se canceló.</summary>
    Task TripStatusChanged(LiveTripStatusUpdate update);
}

/// <summary>
/// Canal en tiempo real (ST-04, QA-5). Conexión: <c>/hubs/trip-tracking?access_token=JWT</c>.
/// El cliente llama <see cref="SubscribeToTrip"/>; se valida con las MISMAS reglas que la consulta REST
/// (el tutor solo entra a viajes de sus hijos) y se devuelve la última posición conocida al instante.
/// </summary>
[Authorize(Policy = Policies.TripViewer)]
public sealed class TripTrackingHub(IDispatcher dispatcher) : Hub<ITripTrackingClient>
{
    public static string Group(Guid tripId) => $"trip:{tripId}";

    public async Task<TripPositionDto> SubscribeToTrip(Guid tripId)
    {
        var requester = Context.User!.ToRequester();
        if (requester.UserId == Guid.Empty) throw new HubException("UNAUTHORIZED");

        var snapshot = await dispatcher.QueryAsync(new GetTripPositionQuery(tripId, requester), Context.ConnectionAborted);
        if (snapshot.IsFailure) throw new HubException(snapshot.FirstError.Code);

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(tripId), Context.ConnectionAborted);
        return snapshot.Value;
    }

    public Task UnsubscribeFromTrip(Guid tripId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(tripId), Context.ConnectionAborted);
}

/// <summary>Adaptador de <see cref="ILiveUpdateSink"/>: lo que llega por RabbitMQ (o en proceso) se empuja al grupo del viaje.</summary>
internal sealed class SignalRLiveUpdateSink(IHubContext<TripTrackingHub, ITripTrackingClient> hub) : ILiveUpdateSink
{
    public Task PositionAsync(LivePositionUpdate update, CancellationToken cancellationToken) =>
        hub.Clients.Group(TripTrackingHub.Group(update.TripId)).PositionUpdated(update);

    public Task StatusAsync(LiveTripStatusUpdate update, CancellationToken cancellationToken) =>
        hub.Clients.Group(TripTrackingHub.Group(update.TripId)).TripStatusChanged(update);
}
