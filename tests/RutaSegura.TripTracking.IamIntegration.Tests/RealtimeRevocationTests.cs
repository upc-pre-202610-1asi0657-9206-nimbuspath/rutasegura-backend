using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RutaSegura.TripTracking.Api.Realtime;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Application.Trips.Queries;
using RutaSegura.TripTracking.Infrastructure.LiveUpdates;
using RutaSegura.TripTracking.IamIntegration.Tests.Support;
namespace RutaSegura.TripTracking.IamIntegration.Tests;
public sealed class RealtimeRevocationTests
{
    [Fact]
    public async Task ExistingSubscriptionStopsReceivingGpsImmediatelyWhenAuthorizationIsRevoked()
    {
        await using var iam = new IamHost();
        var token = await iam.SignInAsync(SeedAccounts.ParentEmail, SeedAccounts.ParentPassword);
        await using var host = new TripTrackingHost(iam);
        var clients = new RecordingClients();
        await using var customized = host.WithWebHostBuilder(builder => builder.ConfigureTestServices(s => s.AddSingleton<IHubContext<TripTrackingHub, ITripTrackingClient>>(new HubContext(clients))));
        _ = customized.CreateClient();
        var trip = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        host.Dispatcher.LiveStatus = new TripLiveStatusDto(trip, "Ruta", "Pickup", "InProgress", now, now,
            new(-12, -77, now, 20, null, 0), null, 0, 1, false, []);
        var subscriptions = customized.Services.GetRequiredService<TripRealtimeSubscriptions>();
        var aborted = false;
        subscriptions.Add(new("open-connection", trip, new Requester(SeedAccounts.Parent, RequesterRole.Guardian, SeedAccounts.Organization), token, now.AddMinutes(5), () => aborted = true));
        var sink = customized.Services.GetRequiredService<ILiveUpdateSink>();
        var update = new LivePositionUpdate(trip, Guid.NewGuid(), Guid.NewGuid(), "InProgress", -12, -77, now, 20, null, null, null, null, 0);
        await sink.PositionAsync(update, TestContext.Current.CancellationToken);
        clients.Delivered.ShouldBe(1); aborted.ShouldBeFalse();
        host.Dispatcher.DenyQueries = true;
        await sink.PositionAsync(update, TestContext.Current.CancellationToken);
        clients.Delivered.ShouldBe(1); aborted.ShouldBeTrue(); subscriptions.ForTrip(trip).ShouldBeEmpty();
    }
    private sealed class HubContext(RecordingClients clients) : IHubContext<TripTrackingHub, ITripTrackingClient>
    {
        public IHubClients<ITripTrackingClient> Clients { get; } = clients;
        public IGroupManager Groups => throw new InvalidOperationException("Authorization must not rely on shared groups.");
    }
    private sealed class RecordingClients : IHubClients<ITripTrackingClient>, ITripTrackingClient
    {
        public int Delivered;
        public Task PositionUpdated(LivePositionUpdate update) { Delivered++; return Task.CompletedTask; }
        public Task TripStatusChanged(LiveTripStatusUpdate update) { Delivered++; return Task.CompletedTask; }
        public ITripTrackingClient Client(string id) => this;
        public ITripTrackingClient All => throw new NotSupportedException();
        public ITripTrackingClient AllExcept(IReadOnlyList<string> ids) => throw new NotSupportedException();
        public ITripTrackingClient Clients(IReadOnlyList<string> ids) => throw new NotSupportedException();
        public ITripTrackingClient Group(string name) => throw new NotSupportedException();
        public ITripTrackingClient GroupExcept(string name, IReadOnlyList<string> ids) => throw new NotSupportedException();
        public ITripTrackingClient Groups(IReadOnlyList<string> names) => throw new NotSupportedException();
        public ITripTrackingClient User(string id) => throw new NotSupportedException();
        public ITripTrackingClient Users(IReadOnlyList<string> ids) => throw new NotSupportedException();
    }
}
