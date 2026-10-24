using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;
using RutaSegura.Monolith.Administration.Tests.Support;
using static RutaSegura.Monolith.Administration.Tests.Support.AdministrationMother;

namespace RutaSegura.Monolith.Administration.Tests.Domain;

public sealed class DriverTests
{
    [Fact]
    public void Register_NewDriver_IsPendingVerificationAndNotAssignable()
    {
        var driver = NewDriver(verified: false);

        driver.Status.IsPendingVerification().ShouldBeTrue();
        driver.IsAssignableOn(Today).ShouldBeFalse();
    }

    [Fact]
    public void Register_WithoutUserAccount_Throws() =>
        Should.Throw<ArgumentException>(() => new Driver(Organization, new UserId(Guid.Empty),
            new FullName("Carlos", "Quispe"), new DocumentNumber("40000001"),
            new DriverLicense("Q12345678", "A-IIb", Today.AddYears(1)), new PhoneNumber("987654321")));

    [Fact]
    public void Verify_ValidLicense_MakesDriverAssignable()
    {
        var driver = NewDriver(verified: false);

        driver.Verify(Today, Now);

        driver.Status.IsVerified().ShouldBeTrue();
        driver.VerifiedAt.ShouldBe(Now);
        driver.IsAssignableOn(Today).ShouldBeTrue();
    }

    [Fact]
    public void Verify_ExpiredLicense_ThrowsDriverLicenseExpired()
    {
        var driver = NewDriver(verified: false, licenseExpiresOn: Today.AddDays(-1));

        Should.Throw<AdministrationDomainException>(() => driver.Verify(Today, Now))
            .Error.ShouldBe(AdministrationError.DriverLicenseExpired);
        driver.Status.IsPendingVerification().ShouldBeTrue();
    }

    [Fact]
    public void Verify_AlreadyVerified_ThrowsInvalidTransition()
    {
        var driver = NewDriver();

        Should.Throw<AdministrationDomainException>(() => driver.Verify(Today, Now))
            .Error.ShouldBe(AdministrationError.DriverInvalidTransition);
    }

    [Fact]
    public void Suspend_ThenReinstate_RestoresAssignability()
    {
        var driver = NewDriver();

        driver.Suspend();
        driver.IsAssignableOn(Today).ShouldBeFalse();

        driver.Reinstate(Today);
        driver.Status.IsVerified().ShouldBeTrue();
        driver.IsAssignableOn(Today).ShouldBeTrue();
    }

    [Fact]
    public void Reinstate_DriverSuspendedBeforeVerification_GoesBackToPending()
    {
        var driver = NewDriver(verified: false);
        driver.Suspend();

        driver.Reinstate(Today);

        driver.Status.IsPendingVerification().ShouldBeTrue();
    }

    [Fact]
    public void IsAssignableOn_AfterLicenseExpires_IsFalse()
    {
        var driver = NewDriver(licenseExpiresOn: Today.AddDays(10));

        driver.IsAssignableOn(Today.AddDays(10)).ShouldBeTrue();
        driver.IsAssignableOn(Today.AddDays(11)).ShouldBeFalse();
    }

    [Fact]
    public void RenewLicense_AlreadyExpiredLicense_Throws()
    {
        var driver = NewDriver();

        Should.Throw<AdministrationDomainException>(() =>
                driver.RenewLicense(new DriverLicense("Q99999999", "A-IIIa", Today.AddDays(-1)), Today))
            .Error.ShouldBe(AdministrationError.DriverLicenseExpired);
    }
}
