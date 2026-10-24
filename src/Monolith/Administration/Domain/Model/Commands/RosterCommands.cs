namespace Monolith.Administration.Domain.Model.Commands;

// Roster commands (drivers and vehicles). OrganizationId always comes from the authenticated identity,
// never from the request body (AC-10).

/// <summary>US-01 S1: registers a driver profile linked to an IAM account with role DRIVER.</summary>
public record RegisterDriverCommand(
    Guid OrganizationId,
    Guid UserId,
    string FirstName,
    string LastName,
    string DocumentNumber,
    string LicenseNumber,
    string LicenseCategory,
    DateOnly LicenseExpiresOn,
    string PhoneNumber);

/// <summary>US-01 S2: an administrator approves the driver's documentation.</summary>
public record VerifyDriverCommand(Guid OrganizationId, Guid DriverId);

public record SuspendDriverCommand(Guid OrganizationId, Guid DriverId);

public record ReinstateDriverCommand(Guid OrganizationId, Guid DriverId);

public record UpdateDriverContactCommand(Guid OrganizationId, Guid DriverId, string PhoneNumber);

public record RenewDriverLicenseCommand(
    Guid OrganizationId,
    Guid DriverId,
    string LicenseNumber,
    string LicenseCategory,
    DateOnly LicenseExpiresOn);

/// <summary>US-08 S1: registers a vehicle and its seating capacity.</summary>
public record RegisterVehicleCommand(Guid OrganizationId, string LicensePlate, int SeatingCapacity, string Brand,
    string Model);

public record ChangeVehicleCapacityCommand(Guid OrganizationId, Guid VehicleId, int SeatingCapacity);

public record EnableVehicleCommand(Guid OrganizationId, Guid VehicleId);

public record DisableVehicleCommand(Guid OrganizationId, Guid VehicleId);
