using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.TripTracking.Api.Security;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Application.Trips.Queries;
using RutaSegura.TripTracking.Infrastructure.LiveUpdates;
namespace RutaSegura.TripTracking.Api.Realtime;
public interface ITripTrackingClient
{
    Task PositionUpdated(LivePositionUpdate update);
    Task TripStatusChanged(LiveTripStatusUpdate update);
}
/// <summary>Ephemeral per-replica subscriptions. Never serialize tokens into logs or persistence.</summary>
public sealed class TripRealtimeSubscriptions
{
    public sealed class Subscription(string connectionId, Guid tripId, Requester requester, string token, DateTimeOffset expires, Action abort)
    {
        public string ConnectionId { get; } = connectionId;
        public Guid TripId { get; } = tripId;
        public Requester Requester { get; } = requester;
        public string Token { get; } = token;
        public DateTimeOffset Expires { get; } = expires;
        public Action Abort { get; } = abort;
        public override string ToString() => "TripSubscription [redacted]";
    }
    private readonly ConcurrentDictionary<(string, Guid), Subscription> subscriptions = new();
    public void Add(Subscription subscription)
    {
        if (subscriptions.Keys.Count(k => k.Item1 == subscription.ConnectionId) >= 50) throw new HubException("SUBSCRIPTION_LIMIT");
        subscriptions[(subscription.ConnectionId, subscription.TripId)] = subscription;
    }
    public Subscription[] ForTrip(Guid trip) => subscriptions.Values.Where(s => s.TripId == trip).ToArray();
    public void Remove(string connection, Guid? trip = null)
    {
        foreach (var key in subscriptions.Keys.Where(k => k.Item1 == connection && (trip is null || k.Item2 == trip))) subscriptions.TryRemove(key, out _);
    }
    public void Reject(Subscription subscription) { Remove(subscription.ConnectionId); subscription.Abort(); }
}
[Authorize(Policy = Policies.TripViewer)]
public sealed class TripTrackingHub(IDispatcher dispatcher, TripRealtimeSubscriptions subscriptions) : Hub<ITripTrackingClient>
{
    public static string Group(Guid tripId) => $"trip:{tripId}";
    public async Task<TripPositionDto> SubscribeToTrip(Guid tripId)
    {
        var requester = Context.User!.ToRequester();
        if (requester.UserId == Guid.Empty) throw new HubException("UNAUTHORIZED");
        var snapshot = await dispatcher.QueryAsync(new GetTripPositionQuery(tripId, requester), Context.ConnectionAborted);
        if (snapshot.IsFailure) throw new HubException(snapshot.FirstError.Code);
        var http = Context.GetHttpContext();
        var token = http is null ? "" : await http.GetTokenAsync("access_token") ?? "";
        if (!long.TryParse(Context.User!.FindFirstValue("exp"), out var seconds)) throw new HubException("UNAUTHORIZED");
        subscriptions.Add(new(Context.ConnectionId, tripId, requester, token, DateTimeOffset.FromUnixTimeSeconds(seconds), Context.Abort));
        // Register before a second authority read to close a revoke-during-subscribe race.
        try
        {
            var checkedSnapshot = await dispatcher.QueryAsync(new GetTripPositionQuery(tripId, requester), Context.ConnectionAborted);
            if (checkedSnapshot.IsFailure) { subscriptions.Remove(Context.ConnectionId, tripId); throw new HubException(checkedSnapshot.FirstError.Code); }
            return checkedSnapshot.Value;
        }
        catch { subscriptions.Remove(Context.ConnectionId, tripId); throw; }
    }
    public Task UnsubscribeFromTrip(Guid tripId) { subscriptions.Remove(Context.ConnectionId, tripId); return Task.CompletedTask; }
    public override Task OnDisconnectedAsync(Exception? exception) { subscriptions.Remove(Context.ConnectionId); return base.OnDisconnectedAsync(exception); }
}
/// <summary>No unfiltered group broadcast: account and current scoped grants are rechecked before every delivery.</summary>
internal sealed class SignalRLiveUpdateSink(IHubContext<TripTrackingHub, ITripTrackingClient> hub, TripRealtimeSubscriptions subscriptions,
    IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock) : ILiveUpdateSink
{
    private async Task<TripLiveStatusDto?> Authorized(TripRealtimeSubscriptions.Subscription session, CancellationToken ct)
    {
        try
        {
            if (session.Expires <= clock.GetUtcNow()) { subscriptions.Reject(session); return null; }
            await using var scope = scopes.CreateAsyncScope();
            if (configuration.GetValue("Iam:AccountValidation:Enabled", true))
            {
                var role = session.Requester.Role == RequesterRole.Guardian ? "PARENT" : session.Requester.Role == RequesterRole.Admin ? "ADMIN" : "DRIVER";
                var identity = new IamTokenIdentity(session.Requester.UserId, role, session.Requester.OrganizationId);
                if ((await scope.ServiceProvider.GetRequiredService<IIamAccountVerifier>().VerifyFreshAsync(session.Token, identity, ct)).State != IamAccountState.Active)
                { subscriptions.Reject(session); return null; }
            }
            var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().QueryAsync(new GetTripLiveStatusQuery(session.TripId, session.Requester), ct);
            if (result.IsFailure) { subscriptions.Reject(session); return null; }
            return result.Value;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { subscriptions.Reject(session); return null; }
    }
    public async Task PositionAsync(LivePositionUpdate update, CancellationToken ct)
    {
        await Parallel.ForEachAsync(subscriptions.ForTrip(update.TripId), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (session, token) =>
        {
            var snapshot = await Authorized(session, token);
            if (snapshot?.Position is null || snapshot.Status is not ("InProgress" or "Delayed") || update.RecordedAt < snapshot.Position.RecordedAt) return;
            var scoped = update with { NextStopId = snapshot.NextStop?.StopId, NextStopDistanceMeters = snapshot.NextStop?.DistanceMeters,
                NextStopEtaMinutes = snapshot.NextStop?.EtaMinutes, StudentsOnBoard = snapshot.StudentsOnBoard };
            try { await hub.Clients.Client(session.ConnectionId).PositionUpdated(scoped).WaitAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { subscriptions.Remove(session.ConnectionId); }
        });
    }
    public async Task StatusAsync(LiveTripStatusUpdate update, CancellationToken ct)
    {
        await Parallel.ForEachAsync(subscriptions.ForTrip(update.TripId), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (session, token) =>
        {
            var snapshot = await Authorized(session, token);
            if (snapshot is null || snapshot.Status != update.Status) return;
            try { await hub.Clients.Client(session.ConnectionId).TripStatusChanged(update).WaitAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { subscriptions.Remove(session.ConnectionId); }
        });
    }
}
