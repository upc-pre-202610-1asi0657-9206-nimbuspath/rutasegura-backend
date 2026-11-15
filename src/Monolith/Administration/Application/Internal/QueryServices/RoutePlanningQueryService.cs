using Monolith.Administration.Application.Internal.OutboundServices;
using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Model.Queries;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Repositories;
using Monolith.Shared.Domain.Model.ValueObjects;
// Microsoft.AspNetCore.Routing (implicit global using of the Web SDK) also defines Route.
using Route = Monolith.Administration.Domain.Model.Aggregates.Route;

namespace Monolith.Administration.Application.Internal.QueryServices;

public class RoutePlanningQueryService(IRouteRepository routeRepository, IPlanLimitsProvider planLimitsProvider)
    : IRoutePlanningQueryService
{
    public async Task<Route?> Handle(GetRouteByIdQuery query, CancellationToken cancellationToken)
    {
        var route = await routeRepository.FindByRouteIdAsync(new RouteId(query.RouteId), cancellationToken);
        return route?.OrganizationId == new OrganizationId(query.OrganizationId) ? route : null;
    }

    public Task<IEnumerable<Route>> Handle(GetAllRoutesQuery query, CancellationToken cancellationToken) =>
        routeRepository.FindAllByOrganizationIdAsync(new OrganizationId(query.OrganizationId), cancellationToken);

    public async Task<IEnumerable<Route>> Handle(GetRoutesByDriverQuery query, CancellationToken cancellationToken)
    {
        var organization = new OrganizationId(query.OrganizationId);
        var routes = await routeRepository.FindAllByDriverIdAsync(new DriverId(query.DriverId), cancellationToken);
        return routes.Where(route => route.OrganizationId == organization).ToList();
    }

    public async Task<ActiveRouteQuota> Handle(GetActiveRouteQuotaQuery query, CancellationToken cancellationToken)
    {
        var organization = new OrganizationId(query.OrganizationId);
        var active = await routeRepository.CountActiveByOrganizationIdAsync(organization, cancellationToken);
        var limits = await planLimitsProvider.GetCurrentLimitsAsync(organization, cancellationToken);
        return new ActiveRouteQuota(limits.PlanCode, active, limits.MaxActiveRoutes);
    }

    public async Task<Route?> Handle(GetRouteByEnrollmentCodeQuery query, CancellationToken cancellationToken)
    {
        var normalized = RouteEnrollmentCode.Normalize(query.EnrollmentCode);
        if (!RouteEnrollmentCode.IsWellFormed(normalized)) return null;
        return await routeRepository.FindByEnrollmentCodeAsync(new RouteEnrollmentCode(normalized), cancellationToken);
    }

    public Task<IEnumerable<Route>> Handle(GetRoutesByIdsQuery query, CancellationToken cancellationToken) =>
        routeRepository.FindAllByIdsAsync(
            (query.RouteIds ?? []).Where(id => id != Guid.Empty).Select(id => new RouteId(id)).ToList(),
            cancellationToken);
}
