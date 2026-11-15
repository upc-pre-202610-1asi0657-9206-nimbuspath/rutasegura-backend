using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Stakeholder.Domain.Model;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Model.Events;
using Monolith.Stakeholder.Domain.Model.Exceptions;
using Monolith.Stakeholder.Domain.Model.ValueObjects;
using static RutaSegura.Monolith.Stakeholder.Tests.Support.StakeholderMother;

namespace RutaSegura.Monolith.Stakeholder.Tests.Domain;

public sealed class StudentTests
{
    private static readonly Guid PickupRoute = Guid.NewGuid();

    [Fact]
    public void Register_RecordsTheGuardianLink()
    {
        var student = NewStudent();

        student.IsGuardedBy(Guardian).ShouldBeTrue();
        student.IsGuardedBy(Guid.NewGuid()).ShouldBeFalse();
        student.Status.ShouldBe(Student.Active);
        var registered = student.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<StudentRegisteredEvent>();
        registered.GuardianUserId.ShouldBe(Guardian);
        registered.StudentId.ShouldBe(student.Id);
    }

    [Theory]
    [InlineData(2025, 1, 1)] // under 2 years old
    [InlineData(2000, 1, 1)] // over 20 years old
    public void Register_WithABirthDateOutsideSchoolAge_IsRejected(int year, int month, int day)
    {
        Should.Throw<ArgumentException>(() => new Student(Guardian, new FullName("Ana", "Quispe"),
            new DateOnly(year, month, day), null, null, Home(), Today, Now));
    }

    [Fact]
    public void Register_TrimsOptionalFields_AndRejectsLongOnes()
    {
        var student = new Student(Guardian, new FullName("Ana", "Quispe"), null, "  ", " Colegio Trilce ", Home(),
            Today, Now);
        student.SchoolGrade.ShouldBeNull();
        student.SchoolName.ShouldBe("Colegio Trilce");

        Should.Throw<ArgumentException>(() => new Student(Guardian, new FullName("Ana", "Quispe"), null,
            new string('x', Student.GradeMaxLength + 1), null, Home(), Today, Now));
    }

    [Fact]
    public void Enroll_AddsAnActiveEnrollment()
    {
        var student = NewStudent();

        var enrollment = student.Enroll(PickupRoute, Organization, "Ruta 3", "pickup", Now);

        enrollment.IsActive().ShouldBeTrue();
        enrollment.Direction.ShouldBe("PICKUP");
        student.IsEnrolledIn(PickupRoute).ShouldBeTrue();
        student.DomainEvents.OfType<StudentEnrolledEvent>().ShouldHaveSingleItem().RouteId.ShouldBe(PickupRoute);
    }

    [Fact]
    public void Enroll_TwiceInTheSameRoute_IsRejected()
    {
        var student = NewStudent();
        student.Enroll(PickupRoute, Organization, "Ruta 3", "PICKUP", Now);

        Should.Throw<StakeholderDomainException>(() => student.Enroll(PickupRoute, Organization, "Ruta 3", "PICKUP", Now))
            .Error.ShouldBe(StakeholderError.StudentAlreadyEnrolled);
    }

    [Fact]
    public void Enroll_InASecondRouteOfTheSameDirection_IsRejected_ButReturnIsAllowed()
    {
        var student = NewStudent();
        student.Enroll(PickupRoute, Organization, "Ruta 3", "PICKUP", Now);

        Should.Throw<StakeholderDomainException>(() =>
                student.Enroll(Guid.NewGuid(), Organization, "Ruta 4", "PICKUP", Now))
            .Error.ShouldBe(StakeholderError.EnrollmentDirectionTaken);
        Should.NotThrow(() => student.Enroll(Guid.NewGuid(), Organization, "Ruta 3 retorno", "RETURN", Now));
        student.ActiveEnrollments.Count.ShouldBe(2);
    }

    [Fact]
    public void Withdraw_EndsTheEnrollment_AndAllowsEnrollingAgain()
    {
        var student = NewStudent();
        var enrollment = student.Enroll(PickupRoute, Organization, "Ruta 3", "PICKUP", Now);

        student.Withdraw(enrollment.Id, Now.AddDays(1));

        enrollment.Status.Value.ShouldBe(EnrollmentStatus.Withdrawn);
        enrollment.EndedAt.ShouldBe(Now.AddDays(1));
        Should.Throw<StakeholderDomainException>(() => student.Withdraw(enrollment.Id, Now))
            .Error.ShouldBe(StakeholderError.EnrollmentNotActive);
        Should.NotThrow(() => student.Enroll(PickupRoute, Organization, "Ruta 3", "PICKUP", Now));
    }

    [Fact]
    public void CancelByOrganization_KeepsTheReason()
    {
        var student = NewStudent();
        var enrollment = student.Enroll(PickupRoute, Organization, "Ruta 3", "PICKUP", Now);

        student.CancelByOrganization(enrollment.Id, Now, "  Servicio impago ");

        enrollment.Status.Value.ShouldBe(EnrollmentStatus.Cancelled);
        enrollment.EndReason.ShouldBe("Servicio impago");
        student.DomainEvents.OfType<StudentEnrollmentEndedEvent>().ShouldHaveSingleItem().Status.ShouldBe("CANCELLED");
    }

    [Fact]
    public void ChangePickupLocation_ReturnsTheRoutesThatMustReplaceTheChild()
    {
        var student = NewStudent();
        var active = student.Enroll(PickupRoute, Organization, "Ruta 3", "PICKUP", Now);
        var ended = student.Enroll(Guid.NewGuid(), Organization, "Ruta 3 retorno", "RETURN", Now);
        student.Withdraw(ended.Id, Now);

        var toUpdate = student.ChangePickupLocation(new PickupLocation("Calle Los Olivos 123", null, -12.1089, -76.9912), Now);

        toUpdate.ShouldHaveSingleItem().ShouldBe(active);
        student.ChangePickupLocation(student.PickupLocation, Now).ShouldBeEmpty("same address: nothing to update");
    }

    [Fact]
    public void Remove_RequiresEndingTheEnrollmentsFirst()
    {
        var student = NewStudent();
        var enrollment = student.Enroll(PickupRoute, Organization, "Ruta 3", "PICKUP", Now);

        Should.Throw<StakeholderDomainException>(() => student.Remove(Now))
            .Error.ShouldBe(StakeholderError.StudentHasActiveEnrollments);

        student.Withdraw(enrollment.Id, Now);
        student.ReplacePhoto(new StudentPhoto(StudentPhoto.Png, 100, Now));
        student.Remove(Now);

        student.IsRemoved().ShouldBeTrue();
        student.Photo.ShouldBeNull();
        student.DomainEvents.OfType<StudentRemovedEvent>().ShouldHaveSingleItem();
        Should.Throw<StakeholderDomainException>(() => student.UpdateDetails(new FullName("A", "B"), null, null, null, Today))
            .Error.ShouldBe(StakeholderError.StudentAlreadyRemoved);
    }
}

public sealed class StakeholderValueObjectTests
{
    [Theory]
    [InlineData("Av. 1", null, -12.1, -76.9, true)]
    [InlineData("Av", null, -12.1, -76.9, false)] // too short
    [InlineData("Av. Primavera 1240", null, 0, 0, false)] // (0,0)
    [InlineData("Av. Primavera 1240", null, 95, -76.9, false)]
    public void PickupLocation_Validation(string address, string? reference, double lat, double lng, bool valid)
    {
        if (valid) Should.NotThrow(() => new PickupLocation(address, reference, lat, lng));
        else Should.Throw<ArgumentException>(() => new PickupLocation(address, reference, lat, lng));
    }

    [Fact]
    public void StudentPhoto_DetectsTheFormatFromTheContent()
    {
        StudentPhoto.DetectContentType([0xFF, 0xD8, 0xFF, 0xE0]).ShouldBe(StudentPhoto.Jpeg);
        StudentPhoto.DetectContentType([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]).ShouldBe(StudentPhoto.Png);
        StudentPhoto.DetectContentType("RIFF\0\0\0\0WEBPVP8 "u8).ShouldBe(StudentPhoto.WebP);
        StudentPhoto.DetectContentType("<svg onload=alert(1)>"u8).ShouldBeNull();
        StudentPhoto.DetectContentType([]).ShouldBeNull();
    }

    [Fact]
    public void StudentPhoto_RejectsBigFilesAndOtherTypes()
    {
        Should.Throw<ArgumentException>(() => new StudentPhoto(StudentPhoto.Png, StudentPhoto.MaxSizeBytes + 1, DateTimeOffset.UtcNow));
        Should.Throw<ArgumentException>(() => new StudentPhoto("image/gif", 10, DateTimeOffset.UtcNow));
        new StudentPhoto(StudentPhoto.Jpeg, 10, DateTimeOffset.UtcNow).Version.Length.ShouldBe(12);
    }

    [Theory]
    [InlineData("987 654 321", "987654321", true)]
    [InlineData("+51 987-654-321", "+51987654321", true)]
    [InlineData("12345", "12345", false)]
    [InlineData("51+987654321", "51+987654321", false)]
    public void GuardianPhoneNumber_Validation(string typed, string normalized, bool valid)
    {
        if (!valid)
        {
            Should.Throw<ArgumentException>(() => new GuardianPhoneNumber(typed));
            return;
        }

        new GuardianPhoneNumber(typed).Value.ShouldBe(normalized);
    }

    [Fact]
    public void GuardianProfile_ValidatesTheDocument()
    {
        var profile = new GuardianProfile(Guid.NewGuid(), new GuardianPhoneNumber("987654321"), " 45781236 ");
        profile.DocumentNumber.ShouldBe("45781236");
        Should.Throw<ArgumentException>(() => profile.Update(new GuardianPhoneNumber("987654321"), "123"));
        profile.Update(new GuardianPhoneNumber("987654321"), null);
        profile.DocumentNumber.ShouldBeNull();
    }
}
