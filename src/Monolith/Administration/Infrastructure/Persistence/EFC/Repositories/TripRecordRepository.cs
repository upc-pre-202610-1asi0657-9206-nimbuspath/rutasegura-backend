using Microsoft.EntityFrameworkCore;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Repositories;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Shared.Infrastructure.Persistence.EFC.Repositories;

namespace Monolith.Administration.Infrastructure.Persistence.EFC.Repositories;

public class TripRecordRepository(AppDbContext context) : BaseRepository<TripRecord>(context), ITripRecordRepository
{
    public Task<TripRecord?> FindByTripIdAsync(TripId tripId, CancellationToken cancellationToken) =>
        Context.Set<TripRecord>().FirstOrDefaultAsync(tripRecord => tripRecord.TripId == tripId, cancellationToken);

    public async Task<IEnumerable<TripRecord>> FindAllByRouteIdAsync(RouteId routeId,
        CancellationToken cancellationToken) =>
        await Context.Set<TripRecord>().Where(tripRecord => tripRecord.RouteId == routeId)
            .OrderByDescending(tripRecord => tripRecord.CompletedAt)
            .ToListAsync(cancellationToken);
}
