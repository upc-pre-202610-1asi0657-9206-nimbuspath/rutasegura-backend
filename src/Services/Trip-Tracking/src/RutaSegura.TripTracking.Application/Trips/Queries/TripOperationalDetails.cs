using System.Text.Json;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;

namespace RutaSegura.TripTracking.Application.Trips.Queries;

public sealed record TripRosterStudent(Guid StudentId, string FullName);
public sealed record TripItinerary(JsonElement Geometry, double DistanceMeters, double DurationSeconds, DateTimeOffset CalculatedAt);
public sealed record TripOperationalDetails(IReadOnlyList<TripRosterStudent> Students, TripItinerary? Itinerary);
public sealed record TripOperationalStop(Guid StopId, double Latitude, double Longitude);

public interface ITripOperationsDirectory
{
    Task<TripOperationalDetails> GetAsync(Guid organizationId, Guid routeId, IReadOnlyList<Guid> studentIds,
        IReadOnlyList<TripOperationalStop> stops, CancellationToken ct);
}

public sealed class TripOperationsDirectoryUnavailableException() : Exception("TRIP_OPERATIONS_DIRECTORY_UNAVAILABLE");

public sealed record GetTripOperationalDetailsQuery(Guid TripId, Requester Requester) : IQuery<TripOperationalDetails>;

internal sealed class GetTripOperationalDetailsQueryHandler(ITripRepository trips, ITripOperationsDirectory directory)
    : IQueryHandler<GetTripOperationalDetailsQuery, TripOperationalDetails>
{
    public async Task<Result<TripOperationalDetails>> HandleAsync(GetTripOperationalDetailsQuery query, CancellationToken ct)
    {
        var loaded = await trips.LoadAsync(query.TripId, ct);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;
        if (trip.OrganizationId.IsEmpty || !query.Requester.CanOperateAsStaff(trip))
            return ApplicationErrors.TripAccessDenied;

        var studentIds = trip.Passengers.Select(passenger => passenger.StudentId.Value).ToArray();
        var stops = trip.Stops.Select(stop => new TripOperationalStop(stop.StopId.Value, stop.Position.Latitude, stop.Position.Longitude)).ToArray();
        return await directory.GetAsync(trip.OrganizationId.Value, trip.RouteId.Value, studentIds, stops, ct);
    }
}
