using Microsoft.EntityFrameworkCore;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Repositories;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Shared.Infrastructure.Persistence.EFC.Repositories;

namespace Monolith.Administration.Infrastructure.Persistence.EFC.Repositories;

public class VehicleRepository(AppDbContext context) : BaseRepository<Vehicle>(context), IVehicleRepository
{
    public Task<Vehicle?> FindByVehicleIdAsync(VehicleId vehicleId, CancellationToken cancellationToken) =>
        Context.Set<Vehicle>().FirstOrDefaultAsync(vehicle => vehicle.Id == vehicleId, cancellationToken);

    public async Task<IEnumerable<Vehicle>> FindAllByOrganizationIdAsync(OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        var vehicles = await Context.Set<Vehicle>().Where(vehicle => vehicle.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        return vehicles.OrderBy(vehicle => vehicle.LicensePlate.Value).ToList();
    }

    public Task<bool> ExistsByLicensePlateAsync(OrganizationId organizationId, LicensePlate licensePlate,
        CancellationToken cancellationToken) =>
        Context.Set<Vehicle>().AnyAsync(
            vehicle => vehicle.OrganizationId == organizationId && vehicle.LicensePlate == licensePlate,
            cancellationToken);
}
