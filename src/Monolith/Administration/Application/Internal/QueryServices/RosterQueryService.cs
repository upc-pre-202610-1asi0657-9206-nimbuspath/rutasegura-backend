using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Queries;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Repositories;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Administration.Application.Internal.QueryServices;

public class RosterQueryService(IDriverRepository driverRepository, IVehicleRepository vehicleRepository)
    : IRosterQueryService
{
    public async Task<Driver?> Handle(GetDriverByIdQuery query, CancellationToken cancellationToken)
    {
        var driver = await driverRepository.FindByDriverIdAsync(new DriverId(query.DriverId), cancellationToken);
        return driver?.OrganizationId == new OrganizationId(query.OrganizationId) ? driver : null;
    }

    public Task<IEnumerable<Driver>> Handle(GetAllDriversQuery query, CancellationToken cancellationToken) =>
        driverRepository.FindAllByOrganizationIdAsync(new OrganizationId(query.OrganizationId), cancellationToken);

    public async Task<Driver?> Handle(GetDriverByUserIdQuery query, CancellationToken cancellationToken)
    {
        var driver = await driverRepository.FindByUserIdAsync(new UserId(query.UserId), cancellationToken);
        return driver?.OrganizationId == new OrganizationId(query.OrganizationId) ? driver : null;
    }

    public async Task<Vehicle?> Handle(GetVehicleByIdQuery query, CancellationToken cancellationToken)
    {
        var vehicle = await vehicleRepository.FindByVehicleIdAsync(new VehicleId(query.VehicleId), cancellationToken);
        return vehicle?.OrganizationId == new OrganizationId(query.OrganizationId) ? vehicle : null;
    }

    public Task<IEnumerable<Vehicle>> Handle(GetAllVehiclesQuery query, CancellationToken cancellationToken) =>
        vehicleRepository.FindAllByOrganizationIdAsync(new OrganizationId(query.OrganizationId), cancellationToken);
}
