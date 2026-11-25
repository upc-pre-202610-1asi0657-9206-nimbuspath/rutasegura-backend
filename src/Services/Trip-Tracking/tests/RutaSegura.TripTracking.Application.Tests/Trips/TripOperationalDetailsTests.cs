using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Application.Tests.Fakes;
using RutaSegura.TripTracking.Application.Trips.Queries;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Tests.Trips;

public sealed class TripOperationalDetailsTests : TripTrackingTestContext
{
    [Theory]
    [InlineData(RequesterRole.Admin)]
    [InlineData(RequesterRole.Driver)]
    public async Task StaffLookupUsesTheTripManifestAndOrderedStopSnapshot(RequesterRole role)
    {
        var trip = SeedStartedTrip(TripDirection.Return, SantaRosa);
        var requester = role == RequesterRole.Admin ? AdminOf(SantaRosa) : new(DriverId, role);
        var directory = new RecordingDirectory();
        var handler = new GetTripOperationalDetailsQueryHandler(Trips, directory);

        var result = await handler.HandleAsync(new(trip.Id.Value, requester), Ct);

        result.IsSuccess.ShouldBeTrue();
        directory.Organization.ShouldBe(SantaRosa);
        directory.Route.ShouldBe(RouteId);
        directory.Students.ShouldBe([Valeria, Mateo, Camila], ignoreOrder: true);
        directory.Stops.ShouldBe([School, Stop2, Stop1]);
        result.Value.Students.Single().FullName.ShouldBe("Valeria Demo");
    }

    [Theory]
    [InlineData(RequesterRole.Admin)]
    [InlineData(RequesterRole.Driver)]
    [InlineData(RequesterRole.Guardian)]
    public async Task UnauthorizedRequesterNeverCallsTheDirectory(RequesterRole role)
    {
        var trip = SeedStartedTrip(organizationId: SantaRosa);
        var requester = role switch
        {
            RequesterRole.Admin => AdminOf(OtherCompany),
            RequesterRole.Driver => new(OtherDriverId, role, SantaRosa),
            _ => new(GuardianOfValeria, role, SantaRosa)
        };
        var directory = new RecordingDirectory();
        var handler = new GetTripOperationalDetailsQueryHandler(Trips, directory);

        var result = await handler.HandleAsync(new(trip.Id.Value, requester), Ct);

        result.FirstError.ShouldBe(ApplicationErrors.TripAccessDenied);
        directory.Organization.ShouldBe(Guid.Empty);
    }

    private sealed class RecordingDirectory : ITripOperationsDirectory
    {
        public Guid Organization { get; private set; }
        public Guid Route { get; private set; }
        public Guid[] Students { get; private set; } = [];
        public Guid[] Stops { get; private set; } = [];

        public Task<TripOperationalDetails> GetAsync(Guid organizationId, Guid routeId, IReadOnlyList<Guid> studentIds,
            IReadOnlyList<TripOperationalStop> stops, CancellationToken ct)
        {
            Organization = organizationId;
            Route = routeId;
            Students = studentIds.ToArray();
            Stops = stops.Select(stop => stop.StopId).ToArray();
            return Task.FromResult(new TripOperationalDetails([new(Valeria, "Valeria Demo")], null));
        }
    }
}
