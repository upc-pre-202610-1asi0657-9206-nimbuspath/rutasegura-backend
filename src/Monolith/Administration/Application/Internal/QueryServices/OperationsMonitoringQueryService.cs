using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Queries;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Repositories;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Administration.Application.Internal.QueryServices;

public class OperationsMonitoringQueryService(
    IOperationalAlertRepository alertRepository,
    ITripRecordRepository tripRecordRepository) : IOperationsMonitoringQueryService
{
    public Task<IEnumerable<OperationalAlert>> Handle(GetOperationalAlertsQuery query,
        CancellationToken cancellationToken) =>
        alertRepository.FindAllByOrganizationIdAsync(new OrganizationId(query.OrganizationId), query.OnlyOpen,
            cancellationToken);

    public async Task<IEnumerable<TripRecord>> Handle(GetTripRecordsByRouteQuery query,
        CancellationToken cancellationToken)
    {
        var organization = new OrganizationId(query.OrganizationId);
        var records = await tripRecordRepository.FindAllByRouteIdAsync(new RouteId(query.RouteId), cancellationToken);
        return records.Where(tripRecord => tripRecord.OrganizationId == organization).ToList();
    }
}
