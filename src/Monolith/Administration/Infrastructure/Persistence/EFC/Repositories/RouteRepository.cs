using Microsoft.EntityFrameworkCore;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Repositories;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Shared.Infrastructure.Persistence.EFC.Repositories;
// Microsoft.AspNetCore.Routing (implicit global using of the Web SDK) also defines Route.
using Route = Monolith.Administration.Domain.Model.Aggregates.Route;

namespace Monolith.Administration.Infrastructure.Persistence.EFC.Repositories;

/// <remarks>Stops (and their students) are auto-included: a Route is always loaded as a whole aggregate.</remarks>
public class RouteRepository(AppDbContext context) : BaseRepository<Route>(context), IRouteRepository
{
    public Task<Route?> FindByRouteIdAsync(RouteId routeId, CancellationToken cancellationToken) =>
        Context.Set<Route>().FirstOrDefaultAsync(route => route.Id == routeId, cancellationToken);

    public async Task<IEnumerable<Route>> FindAllByOrganizationIdAsync(OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        var routes = await Context.Set<Route>().Where(route => route.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        return routes.OrderBy(route => route.Name.Value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IEnumerable<Route>> FindAllByDriverIdAsync(DriverId driverId,
        CancellationToken cancellationToken) =>
        await Context.Set<Route>().Where(route => route.Assignment!.DriverId == driverId)
            .ToListAsync(cancellationToken);

    public async Task<IEnumerable<Route>> FindAllByVehicleIdAsync(VehicleId vehicleId,
        CancellationToken cancellationToken) =>
        await Context.Set<Route>().Where(route => route.Assignment!.VehicleId == vehicleId)
            .ToListAsync(cancellationToken);

    public Task<int> CountActiveByOrganizationIdAsync(OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        var active = RouteStatus.CreateActive();
        return Context.Set<Route>()
            .CountAsync(route => route.OrganizationId == organizationId && route.Status == active, cancellationToken);
    }

    public Task<bool> ExistsByNameAsync(OrganizationId organizationId, RouteName name,
        CancellationToken cancellationToken) =>
        Context.Set<Route>().AnyAsync(route => route.OrganizationId == organizationId && route.Name == name,
            cancellationToken);
}
