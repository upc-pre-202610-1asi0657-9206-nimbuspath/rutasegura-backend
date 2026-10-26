using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Repositories;
// Microsoft.AspNetCore.Routing (implicit global using of the Web SDK) also defines Route.
using Route = Monolith.Administration.Domain.Model.Aggregates.Route;

namespace Monolith.Administration.Domain.Repositories;

public interface IRouteRepository : IBaseRepository<Route>
{
    /// <summary>Loads the route with its stops and their students.</summary>
    Task<Route?> FindByRouteIdAsync(RouteId routeId, CancellationToken cancellationToken);

    Task<IEnumerable<Route>> FindAllByOrganizationIdAsync(OrganizationId organizationId,
        CancellationToken cancellationToken);

    /// <summary>Routes (any status) whose current assignment uses the driver.</summary>
    Task<IEnumerable<Route>> FindAllByDriverIdAsync(DriverId driverId, CancellationToken cancellationToken);

    /// <summary>Routes (any status) whose current assignment uses the vehicle.</summary>
    Task<IEnumerable<Route>> FindAllByVehicleIdAsync(VehicleId vehicleId, CancellationToken cancellationToken);

    /// <summary>ACTIVE routes of the organization; compared with the plan limit.</summary>
    Task<int> CountActiveByOrganizationIdAsync(OrganizationId organizationId, CancellationToken cancellationToken);

    /// <summary>The route families enroll in with <paramref name="code" />, if any.</summary>
    Task<Route?> FindByEnrollmentCodeAsync(RouteEnrollmentCode code, CancellationToken cancellationToken);

    /// <summary>Routes with the given ids (any organization); used by the published facade.</summary>
    Task<IEnumerable<Route>> FindAllByIdsAsync(IReadOnlyCollection<RouteId> routeIds, CancellationToken cancellationToken);

    Task<bool> ExistsByNameAsync(OrganizationId organizationId, RouteName name, CancellationToken cancellationToken);
}
