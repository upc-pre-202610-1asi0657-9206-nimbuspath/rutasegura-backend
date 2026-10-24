using Microsoft.EntityFrameworkCore;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Repositories;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Shared.Infrastructure.Persistence.EFC.Repositories;

namespace Monolith.Administration.Infrastructure.Persistence.EFC.Repositories;

public class OperationalAlertRepository(AppDbContext context)
    : BaseRepository<OperationalAlert>(context), IOperationalAlertRepository
{
    public Task<OperationalAlert?> FindByAlertIdAsync(OperationalAlertId alertId,
        CancellationToken cancellationToken) =>
        Context.Set<OperationalAlert>().FirstOrDefaultAsync(alert => alert.Id == alertId, cancellationToken);

    public Task<OperationalAlert?> FindByIncidentIdAsync(Guid incidentId, CancellationToken cancellationToken) =>
        Context.Set<OperationalAlert>().FirstOrDefaultAsync(alert => alert.IncidentId == incidentId,
            cancellationToken);

    public Task<OperationalAlert?> FindLatestSosByTripIdAsync(TripId tripId, CancellationToken cancellationToken)
    {
        var sos = AlertType.CreateSos();
        return Context.Set<OperationalAlert>()
            .Where(alert => alert.TripId == tripId && alert.Type == sos)
            .OrderByDescending(alert => alert.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<OperationalAlert>> FindAllByOrganizationIdAsync(OrganizationId organizationId,
        bool onlyOpen, CancellationToken cancellationToken)
    {
        var query = Context.Set<OperationalAlert>().Where(alert => alert.OrganizationId == organizationId);
        if (onlyOpen)
        {
            var resolved = AlertStatus.CreateResolved();
            query = query.Where(alert => alert.Status != resolved);
        }

        return await query.OrderByDescending(alert => alert.OccurredAt).ToListAsync(cancellationToken);
    }
}
