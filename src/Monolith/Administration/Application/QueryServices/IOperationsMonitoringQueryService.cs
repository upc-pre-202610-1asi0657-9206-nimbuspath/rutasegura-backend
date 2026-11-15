using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Queries;

namespace Monolith.Administration.Application.QueryServices;

public interface IOperationsMonitoringQueryService
{
    Task<IEnumerable<OperationalAlert>> Handle(GetOperationalAlertsQuery query, CancellationToken cancellationToken);

    Task<IEnumerable<TripRecord>> Handle(GetTripRecordsByRouteQuery query, CancellationToken cancellationToken);
}
