using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Replicas;
using RutaSegura.TripTracking.Application.Tests.Fakes;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Tests.Trips;

/// <summary>
/// <c>administration.route-updated</c> (p. ej. un padre inscribió a su hijo) actualiza los viajes ya programados
/// que aún no empiezan.
/// </summary>
[Trait("Category", "Application")]
public class RouteSnapshotRefreshTests : TripTrackingTestContext
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid Lucia = Guid.Parse("b0000000-0000-0000-0000-000000000004");
    private static readonly Guid NewStop = Guid.Parse("a0000000-0000-0000-0000-000000000009");

    [Fact]
    public async Task Upsert_RefreshesScheduledTrips_ButNotStartedOnes()
    {
        var scheduled = SeedScheduledTrip();
        var started = SeedScheduledTrip();
        started.Start(new DriverId(DriverId), ClientEventId.New(), ScheduledStart);
        var store = new RecordingRouteStore();
        var route = Route3() with
        {
            Stops =
            [
                new RouteStopSnapshot(Stop1, 1, "Av. Primavera 1240", -12.1000, -77.0),
                new RouteStopSnapshot(NewStop, 2, "Av. Benavides 4500", -12.0950, -77.0),
                new RouteStopSnapshot(Stop2, 3, "Jr. Las Begonias 310", -12.0900, -77.0),
                new RouteStopSnapshot(School, 4, "Colegio San Agustín", -12.0800, -77.0)
            ],
            Students = [.. Route3().Students, new RouteStudentSnapshot(Lucia, NewStop)]
        };

        var result = await new UpsertRouteSnapshotCommandHandler(store, Trips)
            .HandleAsync(new UpsertRouteSnapshotCommand(route), Ct);

        result.IsSuccess.ShouldBeTrue();
        store.Upserted.ShouldHaveSingleItem();
        scheduled.HasPassenger(new StudentId(Lucia)).ShouldBeTrue();
        scheduled.Stops.Count.ShouldBe(4);
        started.HasPassenger(new StudentId(Lucia)).ShouldBeFalse();
    }

    private sealed class RecordingRouteStore : IRouteCatalogStore
    {
        public List<RouteSnapshot> Upserted { get; } = [];

        public Task UpsertAsync(RouteSnapshot route, CancellationToken cancellationToken)
        {
            Upserted.Add(route);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(RouteId routeId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
