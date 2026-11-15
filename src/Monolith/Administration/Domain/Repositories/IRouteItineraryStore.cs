using Route = Monolith.Administration.Domain.Model.Aggregates.Route;
namespace Monolith.Administration.Domain.Repositories;
public interface IRouteItineraryStore
{
    Task<bool> TrySaveAsync(Route route, CancellationToken cancellationToken);
}
