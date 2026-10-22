using Microsoft.EntityFrameworkCore;
using Monolith.Iam.Domain.Model.Aggregates;
using Monolith.Iam.Domain.Repositories;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Shared.Infrastructure.Persistence.EFC.Repositories;

namespace Monolith.Iam.Infrastructure.Persistence.EFC.Repositories;

public class OrganizationRepository(AppDbContext context) : BaseRepository<Organization>(context),
    IOrganizationRepository
{
    public Task<Organization?> FindByOrganizationIdAsync(OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        Context.Set<Organization>().FirstOrDefaultAsync(organization => organization.Id == organizationId,
            cancellationToken);
}