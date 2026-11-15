namespace Monolith.Administration.Interfaces.Rest.Resources;

// ── Requests ─────────────────────────────────────────────────────────────────

/// <summary>US-01: the IAM account (role DRIVER) must exist in the administrator's organization.</summary>
public record RegisterDriverResource(
    Guid UserId,
    string FirstName,
    string LastName,
    string DocumentNumber,
    string LicenseNumber,
    string LicenseCategory,
    DateOnly LicenseExpiresOn,
    string PhoneNumber);

public record UpdateDriverContactResource(string PhoneNumber);

public record RenewDriverLicenseResource(string LicenseNumber, string LicenseCategory, DateOnly LicenseExpiresOn);

public record RegisterVehicleResource(string LicensePlate, int SeatingCapacity, string Brand, string Model);

public record ChangeVehicleCapacityResource(int SeatingCapacity);

// ── Responses ────────────────────────────────────────────────────────────────

public record DriverResource(
    Guid Id,
    Guid UserId,
    string FirstName,
    string LastName,
    string DocumentNumber,
    string LicenseNumber,
    string LicenseCategory,
    DateOnly LicenseExpiresOn,
    string PhoneNumber,
    string Status,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? CreatedAt);

public record VehicleResource(
    Guid Id,
    string LicensePlate,
    int SeatingCapacity,
    string Brand,
    string Model,
    string Status,
    DateTimeOffset? CreatedAt);
