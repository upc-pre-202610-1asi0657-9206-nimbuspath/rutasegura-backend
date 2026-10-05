using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Tests.Fakes;

// Test Doubles de los puertos de salida: el hexágono se prueba sin MySQL ni RabbitMQ.

internal sealed class InMemoryTripRepository : ITripRepository
{
    public List<Trip> Trips { get; } = [];

    public Task<Trip?> GetByIdAsync(TripId id, CancellationToken cancellationToken) =>
        Task.FromResult(Trips.FirstOrDefault(t => t.Id == id));

    public Task<Trip?> FindByRouteAndScheduledStartAsync(RouteId routeId, DateTimeOffset scheduledStart, CancellationToken cancellationToken) =>
        Task.FromResult(Trips.FirstOrDefault(t => t.RouteId == routeId && t.ScheduledStart == scheduledStart));

    public Task<IReadOnlyList<Trip>> ListByDriverAsync(DriverId driverId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Trip>>(Trips
            .Where(t => t.DriverId == driverId && t.ScheduledStart >= from && t.ScheduledStart < to)
            .ToList());

    public void Add(Trip trip) => Trips.Add(trip);
}

internal sealed class InMemoryTrackPointRepository : ITrackPointRepository
{
    public List<TrackPoint> Points { get; } = [];

    public Task AddRangeAsync(IReadOnlyCollection<TrackPoint> points, CancellationToken cancellationToken)
    {
        Points.AddRange(points);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TrackPoint>> GetByTripAsync(TripId tripId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TrackPoint>>(Points.Where(p => p.TripId == tripId).ToList());
}

internal sealed class RecordingLivePositionPublisher : ILivePositionPublisher
{
    public List<LivePositionUpdate> Published { get; } = [];

    public Task PublishAsync(LivePositionUpdate update, CancellationToken cancellationToken)
    {
        Published.Add(update);
        return Task.CompletedTask;
    }
}

internal sealed class FakeGuardianDirectory : IGuardianDirectory
{
    private readonly Dictionary<GuardianId, HashSet<StudentId>> _links = [];

    public FakeGuardianDirectory Link(GuardianId guardian, params StudentId[] students)
    {
        if (!_links.TryGetValue(guardian, out var set)) _links[guardian] = set = [];
        set.UnionWith(students);
        return this;
    }

    public Task<IReadOnlyCollection<StudentId>> GetStudentsOfGuardianAsync(GuardianId guardianId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<StudentId>>(_links.TryGetValue(guardianId, out var set) ? set.ToList() : []);
}

internal sealed class FakeRouteCatalog : IRouteCatalog
{
    public Dictionary<Guid, RouteSnapshot> Routes { get; } = [];

    public Task<RouteSnapshot?> GetAsync(RouteId routeId, CancellationToken cancellationToken) =>
        Task.FromResult(Routes.GetValueOrDefault(routeId.Value));
}
