using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Repositories;

namespace Monolith.Administration.Domain.Repositories;

public interface ITripRecordRepository : IBaseRepository<TripRecord>
{
    Task<TripRecord?> FindByTripIdAsync(TripId tripId, CancellationToken cancellationToken);

    Task<IEnumerable<TripRecord>> FindAllByRouteIdAsync(RouteId routeId, CancellationToken cancellationToken);
}
