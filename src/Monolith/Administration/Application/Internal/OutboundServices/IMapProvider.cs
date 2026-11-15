using Monolith.Administration.Domain.Model.ValueObjects;
namespace Monolith.Administration.Application.Internal.OutboundServices;
public interface IMapProvider
{
    Task<RouteItinerary> CalculateAsync(IReadOnlyList<GeoLocation> stops, CancellationToken cancellationToken);
}
public sealed class MapProviderException(string code) : Exception(code) { public string Code { get; } = code; }
