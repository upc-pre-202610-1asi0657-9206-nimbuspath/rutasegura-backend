using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Repositories;

namespace Monolith.Administration.Domain.Repositories;

public interface IVehicleRepository : IBaseRepository<Vehicle>
{
    Task<Vehicle?> FindByVehicleIdAsync(VehicleId vehicleId, CancellationToken cancellationToken);

    Task<IEnumerable<Vehicle>> FindAllByOrganizationIdAsync(OrganizationId organizationId,
        CancellationToken cancellationToken);

    Task<bool> ExistsByLicensePlateAsync(OrganizationId organizationId, LicensePlate licensePlate,
        CancellationToken cancellationToken);
}
