using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Commands;
using Monolith.Shared.Application.Model;

namespace Monolith.Administration.Application.CommandServices;

/// <summary>Roster use cases: manage the drivers and vehicles of the operation.</summary>
public interface IRosterCommandService
{
    Task<Result<Driver>> Handle(RegisterDriverCommand command, CancellationToken cancellationToken);

    Task<Result<Driver>> Handle(VerifyDriverCommand command, CancellationToken cancellationToken);

    Task<Result<Driver>> Handle(SuspendDriverCommand command, CancellationToken cancellationToken);

    Task<Result<Driver>> Handle(ReinstateDriverCommand command, CancellationToken cancellationToken);

    Task<Result<Driver>> Handle(UpdateDriverContactCommand command, CancellationToken cancellationToken);

    Task<Result<Driver>> Handle(RenewDriverLicenseCommand command, CancellationToken cancellationToken);

    Task<Result<Vehicle>> Handle(RegisterVehicleCommand command, CancellationToken cancellationToken);

    Task<Result<Vehicle>> Handle(ChangeVehicleCapacityCommand command, CancellationToken cancellationToken);

    Task<Result<Vehicle>> Handle(EnableVehicleCommand command, CancellationToken cancellationToken);

    Task<Result<Vehicle>> Handle(DisableVehicleCommand command, CancellationToken cancellationToken);
}
