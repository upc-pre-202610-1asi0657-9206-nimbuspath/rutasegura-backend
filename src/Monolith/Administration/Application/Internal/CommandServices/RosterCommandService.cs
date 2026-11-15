using Monolith.Administration.Application.CommandServices;
using Monolith.Administration.Application.Internal.OutboundServices.ACL;
using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Commands;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Repositories;
using Monolith.Shared.Application.Model;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Repositories;

namespace Monolith.Administration.Application.Internal.CommandServices;

public class RosterCommandService(
    IDriverRepository driverRepository,
    IVehicleRepository vehicleRepository,
    IRouteRepository routeRepository,
    IIamAccountService iamAccountService,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRosterCommandService
{
    // ── Drivers ──────────────────────────────────────────────────────────────

    public Task<Result<Driver>> Handle(RegisterDriverCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Driver>(async () =>
        {
            var organizationId = AdministrationOperation.Organization(command.OrganizationId);
            var documentNumber = new DocumentNumber(command.DocumentNumber);
            var userId = new UserId(command.UserId);

            if (await driverRepository.ExistsByDocumentNumberAsync(organizationId, documentNumber, cancellationToken))
                return Result<Driver>.Failure(AdministrationError.DriverAlreadyRegistered,
                    $"A driver with document {documentNumber} is already registered in the organization.");
            if (await driverRepository.ExistsByUserIdAsync(userId, cancellationToken))
                return Result<Driver>.Failure(AdministrationError.DriverAccountAlreadyLinked,
                    "The user account is already linked to another driver profile.");
            if (!await iamAccountService.IsDriverAccountOfOrganizationAsync(command.UserId, command.OrganizationId,
                    cancellationToken))
                return Result<Driver>.Failure(AdministrationError.DriverAccountNotValid,
                    "The user account does not exist, does not have role DRIVER or belongs to another organization.");

            var driver = new Driver(
                organizationId,
                userId,
                new FullName(command.FirstName, command.LastName),
                documentNumber,
                new DriverLicense(command.LicenseNumber, command.LicenseCategory, command.LicenseExpiresOn),
                new PhoneNumber(command.PhoneNumber));

            await driverRepository.AddAsync(driver, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<Driver>.Success(driver);
        });

    public Task<Result<Driver>> Handle(VerifyDriverCommand command, CancellationToken cancellationToken) =>
        ChangeDriverAsync(command.OrganizationId, command.DriverId,
            driver => driver.Verify(AdministrationOperation.Today(clock), clock.GetUtcNow()), cancellationToken);

    public Task<Result<Driver>> Handle(SuspendDriverCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Driver>(async () =>
        {
            var driver = await FindDriverAsync(command.OrganizationId, command.DriverId, cancellationToken);
            if (driver is null) return DriverNotFound();

            var activeRoutes = (await routeRepository.FindAllByDriverIdAsync(driver.Id, cancellationToken))
                .Where(route => route.IsActive()).Select(route => route.Name.Value).ToList();
            if (activeRoutes.Count > 0)
                return Result<Driver>.Failure(AdministrationError.DriverHasActiveRoutes,
                    $"The driver operates active routes ({string.Join(", ", activeRoutes)}); assign another driver first.");

            driver.Suspend();
            driverRepository.Update(driver);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<Driver>.Success(driver);
        });

    public Task<Result<Driver>> Handle(ReinstateDriverCommand command, CancellationToken cancellationToken) =>
        ChangeDriverAsync(command.OrganizationId, command.DriverId,
            driver => driver.Reinstate(AdministrationOperation.Today(clock)), cancellationToken);

    public Task<Result<Driver>> Handle(UpdateDriverContactCommand command, CancellationToken cancellationToken) =>
        ChangeDriverAsync(command.OrganizationId, command.DriverId,
            driver => driver.UpdatePhoneNumber(new PhoneNumber(command.PhoneNumber)), cancellationToken);

    public Task<Result<Driver>> Handle(RenewDriverLicenseCommand command, CancellationToken cancellationToken) =>
        ChangeDriverAsync(command.OrganizationId, command.DriverId,
            driver => driver.RenewLicense(
                new DriverLicense(command.LicenseNumber, command.LicenseCategory, command.LicenseExpiresOn),
                AdministrationOperation.Today(clock)), cancellationToken);

    // ── Vehicles ─────────────────────────────────────────────────────────────

    public Task<Result<Vehicle>> Handle(RegisterVehicleCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Vehicle>(async () =>
        {
            var organizationId = AdministrationOperation.Organization(command.OrganizationId);
            var licensePlate = new LicensePlate(command.LicensePlate);
            if (await vehicleRepository.ExistsByLicensePlateAsync(organizationId, licensePlate, cancellationToken))
                return Result<Vehicle>.Failure(AdministrationError.VehiclePlateAlreadyRegistered,
                    $"A vehicle with plate {licensePlate} is already registered in the organization.");

            var vehicle = new Vehicle(organizationId, licensePlate, new SeatingCapacity(command.SeatingCapacity),
                new VehicleModel(command.Brand, command.Model));
            await vehicleRepository.AddAsync(vehicle, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<Vehicle>.Success(vehicle);
        });

    public Task<Result<Vehicle>> Handle(ChangeVehicleCapacityCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Vehicle>(async () =>
        {
            var vehicle = await FindVehicleAsync(command.OrganizationId, command.VehicleId, cancellationToken);
            if (vehicle is null) return VehicleNotFound();

            var plannedStudents = (await routeRepository.FindAllByVehicleIdAsync(vehicle.Id, cancellationToken))
                .Where(route => route.ReservesResources())
                .Select(route => route.StudentCount)
                .DefaultIfEmpty(0)
                .Max();

            vehicle.ChangeCapacity(new SeatingCapacity(command.SeatingCapacity), plannedStudents);
            vehicleRepository.Update(vehicle);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<Vehicle>.Success(vehicle);
        });

    public Task<Result<Vehicle>> Handle(EnableVehicleCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Vehicle>(async () =>
        {
            var vehicle = await FindVehicleAsync(command.OrganizationId, command.VehicleId, cancellationToken);
            if (vehicle is null) return VehicleNotFound();

            vehicle.Enable();
            vehicleRepository.Update(vehicle);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<Vehicle>.Success(vehicle);
        });

    public Task<Result<Vehicle>> Handle(DisableVehicleCommand command, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Vehicle>(async () =>
        {
            var vehicle = await FindVehicleAsync(command.OrganizationId, command.VehicleId, cancellationToken);
            if (vehicle is null) return VehicleNotFound();

            var activeRoutes = (await routeRepository.FindAllByVehicleIdAsync(vehicle.Id, cancellationToken))
                .Where(route => route.IsActive()).Select(route => route.Name.Value).ToList();
            if (activeRoutes.Count > 0)
                return Result<Vehicle>.Failure(AdministrationError.VehicleHasActiveRoutes,
                    $"The vehicle serves active routes ({string.Join(", ", activeRoutes)}); assign another vehicle first.");

            vehicle.Disable();
            vehicleRepository.Update(vehicle);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<Vehicle>.Success(vehicle);
        });

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task<Result<Driver>> ChangeDriverAsync(Guid organizationId, Guid driverId, Action<Driver> change,
        CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<Driver>(async () =>
        {
            var driver = await FindDriverAsync(organizationId, driverId, cancellationToken);
            if (driver is null) return DriverNotFound();

            change(driver);
            driverRepository.Update(driver);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<Driver>.Success(driver);
        });

    /// <summary>Records of another organization are reported as not found, so their existence is not disclosed.</summary>
    private async Task<Driver?> FindDriverAsync(Guid organizationId, Guid driverId,
        CancellationToken cancellationToken)
    {
        var organization = AdministrationOperation.Organization(organizationId);
        var driver = await driverRepository.FindByDriverIdAsync(new DriverId(driverId), cancellationToken);
        return driver is not null && driver.OrganizationId == organization ? driver : null;
    }

    private async Task<Vehicle?> FindVehicleAsync(Guid organizationId, Guid vehicleId,
        CancellationToken cancellationToken)
    {
        var organization = AdministrationOperation.Organization(organizationId);
        var vehicle = await vehicleRepository.FindByVehicleIdAsync(new VehicleId(vehicleId), cancellationToken);
        return vehicle is not null && vehicle.OrganizationId == organization ? vehicle : null;
    }

    private static Result<Driver> DriverNotFound() =>
        Result<Driver>.Failure(AdministrationError.DriverNotFound, "Driver was not found.");

    private static Result<Vehicle> VehicleNotFound() =>
        Result<Vehicle>.Failure(AdministrationError.VehicleNotFound, "Vehicle was not found.");
}
