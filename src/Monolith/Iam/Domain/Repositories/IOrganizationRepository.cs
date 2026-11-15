using Monolith.Iam.Domain.Model.Aggregates;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Repositories;

namespace Monolith.Iam.Domain.Repositories;

public interface IOrganizationRepository : IBaseRepository<Organization>
{
    Task<Organization?> FindByOrganizationIdAsync(OrganizationId organizationId, CancellationToken cancellationToken);
}