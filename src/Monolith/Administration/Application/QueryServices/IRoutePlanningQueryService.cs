using Monolith.Administration.Domain.Model.Queries;
// Microsoft.AspNetCore.Routing (implicit global using of the Web SDK) also defines Route.
using Route = Monolith.Administration.Domain.Model.Aggregates.Route;

namespace Monolith.Administration.Application.QueryServices;

public interface IRoutePlanningQueryService
{
    Task<Route?> Handle(GetRouteByIdQuery query, CancellationToken cancellationToken);

    Task<IEnumerable<Route>> Handle(GetAllRoutesQuery query, CancellationToken cancellationToken);

    Task<IEnumerable<Route>> Handle(GetRoutesByDriverQuery query, CancellationToken cancellationToken);

    Task<ActiveRouteQuota> Handle(GetActiveRouteQuotaQuery query, CancellationToken cancellationToken);

    Task<Route?> Handle(GetRouteByEnrollmentCodeQuery query, CancellationToken cancellationToken);

    Task<IEnumerable<Route>> Handle(GetRoutesByIdsQuery query, CancellationToken cancellationToken);
}
