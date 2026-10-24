using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Repositories;

namespace Monolith.Administration.Domain.Repositories;

public interface IDriverRepository : IBaseRepository<Driver>
{
    Task<Driver?> FindByDriverIdAsync(DriverId driverId, CancellationToken cancellationToken);

    Task<IEnumerable<Driver>> FindAllByOrganizationIdAsync(OrganizationId organizationId,
        CancellationToken cancellationToken);

    Task<bool> ExistsByDocumentNumberAsync(OrganizationId organizationId, DocumentNumber documentNumber,
        CancellationToken cancellationToken);

    Task<bool> ExistsByUserIdAsync(UserId userId, CancellationToken cancellationToken);

    /// <summary>Driver profile linked to an IAM account (the driver signed in to the mobile app).</summary>
    Task<Driver?> FindByUserIdAsync(UserId userId, CancellationToken cancellationToken);
}
