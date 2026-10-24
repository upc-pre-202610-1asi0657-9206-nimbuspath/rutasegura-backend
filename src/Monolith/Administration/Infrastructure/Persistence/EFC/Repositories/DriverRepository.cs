using Microsoft.EntityFrameworkCore;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Repositories;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Shared.Infrastructure.Persistence.EFC.Repositories;

namespace Monolith.Administration.Infrastructure.Persistence.EFC.Repositories;

public class DriverRepository(AppDbContext context) : BaseRepository<Driver>(context), IDriverRepository
{
    public Task<Driver?> FindByDriverIdAsync(DriverId driverId, CancellationToken cancellationToken) =>
        Context.Set<Driver>().FirstOrDefaultAsync(driver => driver.Id == driverId, cancellationToken);

    public async Task<IEnumerable<Driver>> FindAllByOrganizationIdAsync(OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        var drivers = await Context.Set<Driver>().Where(driver => driver.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        return drivers.OrderBy(driver => driver.FullName.LastName).ThenBy(driver => driver.FullName.FirstName).ToList();
    }

    public Task<bool> ExistsByDocumentNumberAsync(OrganizationId organizationId, DocumentNumber documentNumber,
        CancellationToken cancellationToken) =>
        Context.Set<Driver>().AnyAsync(
            driver => driver.OrganizationId == organizationId && driver.DocumentNumber == documentNumber,
            cancellationToken);

    public Task<bool> ExistsByUserIdAsync(UserId userId, CancellationToken cancellationToken) =>
        Context.Set<Driver>().AnyAsync(driver => driver.UserId == userId, cancellationToken);

    public Task<Driver?> FindByUserIdAsync(UserId userId, CancellationToken cancellationToken) =>
        Context.Set<Driver>().FirstOrDefaultAsync(driver => driver.UserId == userId, cancellationToken);
}
