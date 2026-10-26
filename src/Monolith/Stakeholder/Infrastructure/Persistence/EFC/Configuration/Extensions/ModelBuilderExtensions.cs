using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Model.Entities;
using Monolith.Stakeholder.Domain.Model.ValueObjects;
using Monolith.Stakeholder.Infrastructure.Messaging;

namespace Monolith.Stakeholder.Infrastructure.Persistence.EFC.Configuration.Extensions;

/// <summary>
///     EF Core model of the Stakeholder bounded context (tables <c>stakeholder_*</c> after the snake_case convention).
///     The domain is mapped without changes: value objects as complex types or converted columns and the enrollments
///     through the private backing field of the Student aggregate.
/// </summary>
public static class ModelBuilderExtensions
{
    private const string Namespace = "Monolith.Stakeholder";

    private static readonly ValueConverter<StudentId, Guid> StudentIdConverter =
        new(id => id.Identifier, value => new StudentId(value));

    private static readonly ValueConverter<EnrollmentId, Guid> EnrollmentIdConverter =
        new(id => id.Identifier, value => new EnrollmentId(value));

    private static readonly ValueConverter<DateOnly, DateTime> DateOnlyConverter = new(
        value => value.ToDateTime(TimeOnly.MinValue),
        value => DateOnly.FromDateTime(value));

    private static readonly ValueConverter<DateTimeOffset, DateTime> UtcDateTimeConverter = new(
        value => value.UtcDateTime,
        value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    public static void ApplyStakeholderConfiguration(this ModelBuilder builder)
    {
        builder.Entity<Student>(student =>
        {
            student.ToTable("StakeholderStudents");
            student.HasKey(s => s.Id);
            student.Property(s => s.Id).HasConversion(StudentIdConverter).ValueGeneratedNever();
            student.Property(s => s.GuardianUserId).IsRequired();
            student.ComplexProperty(s => s.FullName, name =>
            {
                name.IsRequired();
                name.Property(n => n.FirstName).HasColumnName("FirstName").HasMaxLength(FullName.MaxLength).IsRequired();
                name.Property(n => n.LastName).HasColumnName("LastName").HasMaxLength(FullName.MaxLength).IsRequired();
            });
            student.Property(s => s.BirthDate).HasConversion(DateOnlyConverter).HasColumnType("date");
            student.Property(s => s.SchoolGrade).HasMaxLength(Student.GradeMaxLength);
            student.Property(s => s.SchoolName).HasMaxLength(Student.SchoolMaxLength);
            student.ComplexProperty(s => s.PickupLocation, location =>
            {
                location.IsRequired();
                location.Property(l => l.Address).HasColumnName("PickupAddress")
                    .HasMaxLength(PickupLocation.AddressMaxLength).IsRequired();
                location.Property(l => l.Reference).HasColumnName("PickupReference")
                    .HasMaxLength(PickupLocation.ReferenceMaxLength);
                location.Property(l => l.Latitude).HasColumnName("PickupLatitude");
                location.Property(l => l.Longitude).HasColumnName("PickupLongitude");
            });
            // Optional: the four columns are NULL while the child has no photo.
            student.ComplexProperty(s => s.Photo, photo =>
            {
                photo.Property(p => p.ContentType).HasColumnName("PhotoContentType").HasMaxLength(16);
                photo.Property(p => p.SizeBytes).HasColumnName("PhotoSizeBytes");
                photo.Property(p => p.UploadedAt).HasColumnName("PhotoUploadedAt");
                photo.Property(p => p.Version).HasColumnName("PhotoVersion").HasMaxLength(16);
            });
            student.Property(s => s.Status).HasMaxLength(10).IsRequired();

            student.Ignore(s => s.Enrollments);
            student.Ignore(s => s.ActiveEnrollments);
            student.Ignore(s => s.DomainEvents);

            // MySQL identifiers are limited to 64 characters: the generated FK name would be 65.
            student.HasMany<RouteEnrollment>("_enrollments").WithOne().HasForeignKey("StudentId")
                .HasConstraintName("fk_stakeholder_route_enrollments_student_id")
                .OnDelete(DeleteBehavior.Cascade);
            student.Navigation("_enrollments").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

            student.HasIndex(s => new { s.GuardianUserId, s.Status });
        });

        builder.Entity<RouteEnrollment>(enrollment =>
        {
            enrollment.ToTable("StakeholderRouteEnrollments");
            enrollment.HasKey(e => e.Id);
            enrollment.Property(e => e.Id).HasConversion(EnrollmentIdConverter).ValueGeneratedNever();
            enrollment.Property<StudentId>("StudentId").HasConversion(StudentIdConverter).IsRequired();
            enrollment.Property(e => e.RouteId).IsRequired();
            enrollment.Property(e => e.OrganizationId).IsRequired();
            enrollment.Property(e => e.RouteName).HasMaxLength(RouteEnrollment.RouteNameMaxLength).IsRequired();
            enrollment.Property(e => e.Direction).HasMaxLength(10).IsRequired();
            enrollment.Property(e => e.Status)
                .HasConversion(status => status.Value, value => new EnrollmentStatus(value, false))
                .HasMaxLength(12).IsRequired();
            enrollment.Property(e => e.EndReason).HasMaxLength(RouteEnrollment.EndReasonMaxLength);

            enrollment.HasIndex(e => new { e.RouteId, e.Status });
            enrollment.HasIndex(e => new { e.OrganizationId, e.Status });
        });

        builder.Entity<GuardianProfile>(profile =>
        {
            profile.ToTable("StakeholderGuardianProfiles");
            profile.HasKey(p => p.UserId);
            profile.Property(p => p.UserId).ValueGeneratedNever();
            profile.Property(p => p.PhoneNumber)
                .HasConversion(phone => phone.Value, value => new GuardianPhoneNumber(value, false))
                .HasMaxLength(GuardianPhoneNumber.MaxLength).IsRequired();
            profile.Property(p => p.DocumentNumber).HasMaxLength(GuardianProfile.DocumentMaxLength);
        });

        builder.Entity<StudentPhotoRecord>(photo =>
        {
            photo.ToTable("StakeholderStudentPhotos");
            photo.HasKey(p => p.StudentId);
            photo.Property(p => p.StudentId).ValueGeneratedNever();
            photo.Property(p => p.ContentType).HasMaxLength(16).IsRequired();
            photo.Property(p => p.Content).HasColumnType("mediumblob").IsRequired();
        });

        builder.Entity<StakeholderOutboxMessage>(message =>
        {
            message.ToTable("StakeholderOutboxMessages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).ValueGeneratedNever();
            message.Property(m => m.MessageType).HasMaxLength(100).IsRequired();
            message.Property(m => m.Payload).HasColumnType("longtext").IsRequired();
            message.Property(m => m.CorrelationId).HasMaxLength(64).IsRequired();
            message.Property(m => m.LastError).HasMaxLength(1000);
            message.HasIndex(m => new { m.ProcessedAt, m.NextAttemptAt });
        });

        StoreInstantsAsUtc(builder);
    }

    /// <summary>Every DateTimeOffset of this context (including complex types) is stored as UTC.</summary>
    private static void StoreInstantsAsUtc(ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes()
                     .Where(e => e.ClrType.Namespace?.StartsWith(Namespace, StringComparison.Ordinal) == true))
        {
            var properties = entity.GetProperties()
                .Concat(entity.GetComplexProperties().SelectMany(complex => complex.ComplexType.GetProperties()));
            foreach (var property in properties.Where(p =>
                         p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            {
                property.SetValueConverter(UtcDateTimeConverter);
                property.SetColumnType("datetime(6)");
            }
        }
    }
}
