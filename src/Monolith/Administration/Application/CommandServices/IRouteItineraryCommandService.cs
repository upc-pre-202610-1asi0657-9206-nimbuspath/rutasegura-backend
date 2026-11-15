using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Application.Model;
namespace Monolith.Administration.Application.CommandServices;
public interface IRouteItineraryCommandService
{
    Task<Result<RouteItinerary>> CalculateAsync(Guid organizationId, Guid routeId, CancellationToken ct);
}
