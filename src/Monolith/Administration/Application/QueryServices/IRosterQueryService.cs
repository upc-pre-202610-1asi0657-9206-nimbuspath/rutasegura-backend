using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Queries;

namespace Monolith.Administration.Application.QueryServices;

public interface IRosterQueryService
{
    Task<Driver?> Handle(GetDriverByIdQuery query, CancellationToken cancellationToken);

    Task<IEnumerable<Driver>> Handle(GetAllDriversQuery query, CancellationToken cancellationToken);

    Task<Driver?> Handle(GetDriverByUserIdQuery query, CancellationToken cancellationToken);

    Task<Vehicle?> Handle(GetVehicleByIdQuery query, CancellationToken cancellationToken);

    Task<IEnumerable<Vehicle>> Handle(GetAllVehiclesQuery query, CancellationToken cancellationToken);
}
