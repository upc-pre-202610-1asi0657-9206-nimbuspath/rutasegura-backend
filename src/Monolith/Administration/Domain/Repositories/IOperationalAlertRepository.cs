using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Repositories;

namespace Monolith.Administration.Domain.Repositories;

public interface IOperationalAlertRepository : IBaseRepository<OperationalAlert>
{
    Task<OperationalAlert?> FindByAlertIdAsync(OperationalAlertId alertId, CancellationToken cancellationToken);

    Task<OperationalAlert?> FindByIncidentIdAsync(Guid incidentId, CancellationToken cancellationToken);

    /// <summary>Most recent SOS alert of the trip, whatever its status.</summary>
    Task<OperationalAlert?> FindLatestSosByTripIdAsync(TripId tripId, CancellationToken cancellationToken);

    Task<IEnumerable<OperationalAlert>> FindAllByOrganizationIdAsync(OrganizationId organizationId, bool onlyOpen,
        CancellationToken cancellationToken);
}
