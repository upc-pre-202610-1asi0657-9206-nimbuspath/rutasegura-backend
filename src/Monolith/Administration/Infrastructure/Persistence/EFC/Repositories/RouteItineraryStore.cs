using Microsoft.EntityFrameworkCore;
using Monolith.Administration.Domain.Repositories;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Route = Monolith.Administration.Domain.Model.Aggregates.Route;
namespace Monolith.Administration.Infrastructure.Persistence.EFC.Repositories;
public sealed class RouteItineraryStore(AppDbContext db) : IRouteItineraryStore
{
    public async Task<bool> TrySaveAsync(Route route, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }
}
