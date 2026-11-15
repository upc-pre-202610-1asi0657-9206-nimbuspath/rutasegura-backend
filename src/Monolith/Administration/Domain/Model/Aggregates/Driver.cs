using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Administration.Domain.Model.Aggregates;

/// <summary>
///     Roster aggregate: profile of a driver of the operation (US-01).
/// </summary>
/// <remarks>
///     The driver is linked to an IAM user account with role DRIVER. That account id is the one the mobile
///     app authenticates with, so it is what Trip &amp; Tracking receives as <c>driverId</c>.
///     A driver starts PENDING_VERIFICATION and can only be assigned to routes once VERIFIED and while the
///     driving licence is valid.
/// </remarks>
public partial class Driver
{
    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected Driver()
    {
        Id = new DriverId();
        OrganizationId = new OrganizationId();
        UserId = new UserId();
        FullName = new FullName();
        DocumentNumber = new DocumentNumber();
        License = new DriverLicense();
        PhoneNumber = new PhoneNumber();
        Status = DriverStatus.CreatePendingVerification();
    }

    /// <summary>Registers a new driver pending verification.</summary>
    public Driver(OrganizationId organizationId, UserId userId, FullName fullName, DocumentNumber documentNumber,
        DriverLicense license, PhoneNumber phoneNumber)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        ArgumentNullException.ThrowIfNull(userId);
        if (organizationId.Identifier == Guid.Empty)
            throw new ArgumentException("The driver must belong to an organization.", nameof(organizationId));
        if (userId.Identifier == Guid.Empty)
            throw new ArgumentException("The driver must be linked to an IAM user account.", nameof(userId));

        Id = DriverId.New();
        OrganizationId = organizationId;
        UserId = userId;
        FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
        DocumentNumber = documentNumber ?? throw new ArgumentNullException(nameof(documentNumber));
        License = license ?? throw new ArgumentNullException(nameof(license));
        PhoneNumber = phoneNumber ?? throw new ArgumentNullException(nameof(phoneNumber));
        Status = DriverStatus.CreatePendingVerification();
    }

    public DriverId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>IAM account of the driver (role DRIVER).</summary>
    public UserId UserId { get; private set; }

    public FullName FullName { get; private set; }

    public DocumentNumber DocumentNumber { get; private set; }

    public DriverLicense License { get; private set; }

    public PhoneNumber PhoneNumber { get; private set; }

    public DriverStatus Status { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>US-01 S2: an authorized administrator approves the documentation.</summary>
    public void Verify(DateOnly today, DateTimeOffset verifiedAt)
    {
        if (!Status.IsPendingVerification())
            throw new AdministrationDomainException(AdministrationError.DriverInvalidTransition,
                $"Only drivers pending verification can be verified (current status: {Status}).");
        EnsureLicenseIsValidOn(today);
        Status = DriverStatus.CreateVerified();
        VerifiedAt = verifiedAt;
    }

    /// <summary>Removes the driver from operation. The application layer checks that no active route uses it.</summary>
    public void Suspend()
    {
        if (Status.IsSuspended())
            throw new AdministrationDomainException(AdministrationError.DriverInvalidTransition,
                "The driver is already suspended.");
        Status = DriverStatus.CreateSuspended();
    }

    /// <summary>Brings a suspended driver back to operation; the licence must still be valid.</summary>
    public void Reinstate(DateOnly today)
    {
        if (!Status.IsSuspended())
            throw new AdministrationDomainException(AdministrationError.DriverInvalidTransition,
                $"Only suspended drivers can be reinstated (current status: {Status}).");
        EnsureLicenseIsValidOn(today);
        Status = VerifiedAt is null ? DriverStatus.CreatePendingVerification() : DriverStatus.CreateVerified();
    }

    public void UpdatePhoneNumber(PhoneNumber phoneNumber) =>
        PhoneNumber = phoneNumber ?? throw new ArgumentNullException(nameof(phoneNumber));

    /// <summary>Replaces the driving licence with a renewed one.</summary>
    public void RenewLicense(DriverLicense license, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(license);
        if (!license.IsValidOn(today))
            throw new AdministrationDomainException(AdministrationError.DriverLicenseExpired,
                $"The new driver license expired on {license.ExpiresOn:yyyy-MM-dd}.");
        License = license;
    }

    /// <summary>A driver can operate a route only when verified and with a valid licence on that date.</summary>
    public bool IsAssignableOn(DateOnly date) => Status.IsVerified() && License.IsValidOn(date);

    public string GetFullName() => FullName.ToString();

    private void EnsureLicenseIsValidOn(DateOnly today)
    {
        if (!License.IsValidOn(today))
            throw new AdministrationDomainException(AdministrationError.DriverLicenseExpired,
                $"The driver license expired on {License.ExpiresOn:yyyy-MM-dd}.");
    }
}
