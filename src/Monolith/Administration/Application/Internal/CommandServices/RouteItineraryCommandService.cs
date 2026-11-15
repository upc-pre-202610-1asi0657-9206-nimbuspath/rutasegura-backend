using Monolith.Administration.Application.CommandServices;
using Monolith.Administration.Application.Internal.OutboundServices;
using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Repositories;
using Monolith.Shared.Application.Model;
namespace Monolith.Administration.Application.Internal.CommandServices;
public sealed class RouteItineraryCommandService(IRouteRepository routes, IMapProvider maps, IRouteItineraryStore store)
    : IRouteItineraryCommandService
{
    public async Task<Result<RouteItinerary>> CalculateAsync(Guid organizationId, Guid routeId, CancellationToken ct)
    {
        var route = await routes.FindByRouteIdAsync(new RouteId(routeId), ct);
        if (route is null || route.OrganizationId.Identifier != organizationId)
            return Result<RouteItinerary>.Failure(AdministrationError.RouteNotFound, "Route was not found.");
        if (route.Stops.Count < 2) return Result<RouteItinerary>.Failure(AdministrationError.InvalidAdministrationData, "At least two ordered stops are required.");
        try
        {
            var itinerary = await maps.CalculateAsync(route.Stops.Select(s => s.Location).ToArray(), ct);
            route.SetItinerary(itinerary);
            if (!await store.TrySaveAsync(route, ct))
                return Result<RouteItinerary>.Failure(AdministrationError.RouteItineraryChanged, "Stops changed during calculation. Reload the route and retry.");
            return Result<RouteItinerary>.Success(itinerary);
        }
        catch (MapProviderException ex) { return Result<RouteItinerary>.Failure(AdministrationError.MapProviderUnavailable, ex.Code); }
    }
}
