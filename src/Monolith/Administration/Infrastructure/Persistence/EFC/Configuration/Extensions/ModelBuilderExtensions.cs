using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Entities;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Infrastructure.Messaging.Inbound;
using Monolith.Administration.Infrastructure.Messaging.Outbox;
using Monolith.Shared.Domain.Model.ValueObjects;
using C = Monolith.Administration.Infrastructure.Persistence.EFC.Configuration.AdministrationValueConverters;
// Microsoft.AspNetCore.Routing (implicit global using of the Web SDK) also defines Route and RouteDirection.
using Route = Monolith.Administration.Domain.Model.Aggregates.Route;
using RouteDirection = Monolith.Administration.Domain.Model.ValueObjects.RouteDirection;

namespace Monolith.Administration.Infrastructure.Persistence.EFC.Configuration.Extensions;

/// <summary>
///     EF Core model of the Administration bounded context. Tables keep the module prefix of the architecture
///     report (roster_, route_, operations_) after the shared snake_case convention runs. The domain is mapped
///     without changes: value objects are complex types or converted columns and child collections are read and
///     written through their private backing fields.
/// </summary>
public static class ModelBuilderExtensions
{
    private const string Namespace = "Monolith.Administration";

    public static void ApplyAdministrationConfiguration(this ModelBuilder builder)
    {
        ConfigureRoster(builder);
        ConfigureRoutePlanning(builder);
        ConfigureOperations(builder);
        ConfigureMessaging(builder);
        StoreInstantsAsUtc(builder);
    }

    // ── Roster ───────────────────────────────────────────────────────────────

    private static void ConfigureRoster(ModelBuilder builder)
    {
        builder.Entity<Driver>(driver =>
        {
            driver.ToTable("RosterDrivers");
            driver.HasKey(d => d.Id);
            driver.Property(d => d.Id).HasConversion(C.DriverIdConverter).ValueGeneratedNever();
            driver.Property(d => d.OrganizationId).HasConversion(C.OrganizationIdConverter).IsRequired();
            driver.Property(d => d.UserId).HasConversion(C.UserIdConverter).IsRequired();
            driver.ComplexProperty(d => d.FullName, name =>
            {
                name.IsRequired();
                name.Property(n => n.FirstName).HasColumnName("FirstName").HasMaxLength(FullName.MaxLength)
                    .IsRequired();
                name.Property(n => n.LastName).HasColumnName("LastName").HasMaxLength(FullName.MaxLength)
                    .IsRequired();
            });
            driver.Property(d => d.DocumentNumber)
                .HasConversion(document => document.Value, value => new DocumentNumber(value, false))
                .HasMaxLength(DocumentNumber.MaxLength).IsRequired();
            driver.ComplexProperty(d => d.License, license =>
            {
                license.IsRequired();
                license.Property(l => l.Number).HasColumnName("LicenseNumber").HasMaxLength(12).IsRequired();
                license.Property(l => l.Category).HasColumnName("LicenseCategory").HasMaxLength(8).IsRequired();
                license.Property(l => l.ExpiresOn).HasColumnName("LicenseExpiresOn").HasConversion(C.DateOnlyConverter)
                    .HasColumnType("date");
            });
            driver.Property(d => d.PhoneNumber)
                .HasConversion(phone => phone.Value, value => new PhoneNumber(value, false))
                .HasMaxLength(16).IsRequired();
            driver.Property(d => d.Status)
                .HasConversion(status => status.Value, value => new DriverStatus(value, false))
                .HasMaxLength(24).IsRequired();

            driver.HasIndex(d => d.UserId).IsUnique();
            driver.HasIndex(d => new { d.OrganizationId, d.DocumentNumber }).IsUnique();
        });

        builder.Entity<Vehicle>(vehicle =>
        {
            vehicle.ToTable("RosterVehicles");
            vehicle.HasKey(v => v.Id);
            vehicle.Property(v => v.Id).HasConversion(C.VehicleIdConverter).ValueGeneratedNever();
            vehicle.Property(v => v.OrganizationId).HasConversion(C.OrganizationIdConverter).IsRequired();
            vehicle.Property(v => v.LicensePlate)
                .HasConversion(plate => plate.Value, value => new LicensePlate(value, false))
                .HasMaxLength(7).IsRequired();
            vehicle.Property(v => v.Capacity)
                .HasConversion(capacity => capacity.Value, value => new SeatingCapacity(value, false))
                .HasColumnName("SeatingCapacity").IsRequired();
            vehicle.ComplexProperty(v => v.Model, model =>
            {
                model.IsRequired();
                model.Property(m => m.Brand).HasColumnName("Brand").HasMaxLength(VehicleModel.MaxLength).IsRequired();
                model.Property(m => m.Model).HasColumnName("Model").HasMaxLength(VehicleModel.MaxLength).IsRequired();
            });
            vehicle.Property(v => v.Status)
                .HasConversion(status => status.Value, value => new VehicleStatus(value, false))
                .HasMaxLength(12).IsRequired();

            vehicle.HasIndex(v => new { v.OrganizationId, v.LicensePlate }).IsUnique();
        });
    }

    // ── Route Planning ───────────────────────────────────────────────────────

    private static void ConfigureRoutePlanning(ModelBuilder builder)
    {
        builder.Entity<Route>(route =>
        {
            route.ToTable("RouteItineraries");
            route.HasKey(r => r.Id);
            route.Property(r => r.Id).HasConversion(C.RouteIdConverter).ValueGeneratedNever();
            route.Property(r => r.OrganizationId).HasConversion(C.OrganizationIdConverter).IsRequired();
            route.Property(r => r.Name).HasConversion(name => name.Value, value => new RouteName(value, false))
                .HasMaxLength(RouteName.MaxLength).IsRequired();
            route.Property(r => r.Direction)
                .HasConversion(direction => direction.Value, value => new RouteDirection(value, false))
                .HasMaxLength(10).IsRequired();
            route.Property(r => r.Status).HasConversion(status => status.Value, value => new RouteStatus(value, false))
                .HasMaxLength(10).IsRequired();

            route.ComplexProperty(r => r.Schedule, schedule =>
            {
                schedule.IsRequired();
                schedule.Property(s => s.DepartureTime).HasColumnName("DepartureTime").HasConversion(C.TimeOnlyConverter)
                    .HasColumnType("time(6)");
                schedule.Property(s => s.EstimatedArrivalTime).HasColumnName("EstimatedArrivalTime")
                    .HasConversion(C.TimeOnlyConverter).HasColumnType("time(6)");
                schedule.Property(s => s.TimeZoneId).HasColumnName("TimeZoneId").HasMaxLength(64).IsRequired();
                schedule.Property(s => s.OperatingDaysMask).HasColumnName("OperatingDaysMask");
            });

            // Optional: a DRAFT route may have no driver/vehicle yet (all three columns NULL).
            route.ComplexProperty(r => r.Assignment, assignment =>
            {
                assignment.Property(a => a.DriverId).HasColumnName("AssignedDriverId").HasConversion(C.DriverIdConverter);
                assignment.Property(a => a.VehicleId).HasColumnName("AssignedVehicleId").HasConversion(C.VehicleIdConverter);
                assignment.Property(a => a.AssignedAt).HasColumnName("AssignedAt");
            });

            route.Ignore(r => r.Stops);
            route.Ignore(r => r.StudentCount);
            route.Ignore(r => r.DomainEvents);

            route.HasMany<RouteStop>("_stops").WithOne().HasForeignKey("RouteId").OnDelete(DeleteBehavior.Cascade);
            route.Navigation("_stops").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

            route.HasIndex(r => new { r.OrganizationId, r.Name }).IsUnique();
            route.HasIndex(r => new { r.OrganizationId, r.Status });
        });

        builder.Entity<RouteStop>(stop =>
        {
            stop.ToTable("RouteStops");
            stop.HasKey(s => s.Id);
            stop.Property(s => s.Id).HasConversion(C.RouteStopIdConverter).ValueGeneratedNever();
            stop.Property<RouteId>("RouteId").HasConversion(C.RouteIdConverter).IsRequired();
            stop.Property(s => s.Sequence).IsRequired();
            stop.Property(s => s.Name).HasMaxLength(RouteStop.NameMaxLength).IsRequired();
            stop.Property(s => s.Address).HasMaxLength(RouteStop.AddressMaxLength);
            stop.ComplexProperty(s => s.Location, location =>
            {
                location.IsRequired();
                location.Property(l => l.Latitude).HasColumnName("Latitude");
                location.Property(l => l.Longitude).HasColumnName("Longitude");
            });
            stop.Ignore(s => s.Students);
            stop.Property<List<StudentId>>("_students")
                .HasColumnName("StudentIds")
                .HasColumnType("longtext")
                .HasConversion(C.StudentIdsConverter, C.StudentIdsComparer)
                .IsRequired();
        });
    }

    // ── Operations monitoring (events from Trip & Tracking) ─────────────────

    private static void ConfigureOperations(ModelBuilder builder)
    {
        builder.Entity<OperationalAlert>(alert =>
        {
            alert.ToTable("OperationsAlerts");
            alert.HasKey(a => a.Id);
            alert.Property(a => a.Id).HasConversion(C.OperationalAlertIdConverter).ValueGeneratedNever();
            alert.Property(a => a.OrganizationId).HasConversion(C.OrganizationIdConverter).IsRequired();
            alert.Property(a => a.Type).HasConversion(type => type.Value, value => new AlertType(value, false))
                .HasMaxLength(12).IsRequired();
            alert.Property(a => a.Severity)
                .HasConversion(severity => severity.Value, value => new AlertSeverity(value, false))
                .HasMaxLength(12).IsRequired();
            alert.Property(a => a.Status).HasConversion(status => status.Value, value => new AlertStatus(value, false))
                .HasMaxLength(16).IsRequired();
            alert.Property(a => a.TripId).HasConversion(C.TripIdConverter).IsRequired();
            alert.Property(a => a.RouteId).HasConversion(C.RouteIdConverter).IsRequired();
            alert.Property(a => a.RouteName).HasMaxLength(RouteName.MaxLength).IsRequired();
            alert.Property(a => a.Category)
                .HasConversion(category => category!.Value, value => new IncidentCategory(value, false))
                .HasMaxLength(24);
            alert.Property(a => a.Detail).HasMaxLength(OperationalAlert.DetailMaxLength);
            alert.ComplexProperty(a => a.Location, location =>
            {
                location.Property(l => l.Latitude).HasColumnName("Latitude");
                location.Property(l => l.Longitude).HasColumnName("Longitude");
            });
            alert.Property(a => a.AcknowledgedBy).HasConversion(user => user!.Identifier, value => new UserId(value));

            alert.HasIndex(a => a.IncidentId).IsUnique();
            alert.HasIndex(a => a.TripId);
            alert.HasIndex(a => new { a.OrganizationId, a.Status });
        });

        builder.Entity<TripRecord>(record =>
        {
            record.ToTable("OperationsTripRecords");
            record.HasKey(r => r.Id);
            record.Property(r => r.Id).HasConversion(C.TripRecordIdConverter).ValueGeneratedNever();
            record.Property(r => r.OrganizationId).HasConversion(C.OrganizationIdConverter).IsRequired();
            record.Property(r => r.TripId).HasConversion(C.TripIdConverter).IsRequired();
            record.Property(r => r.RouteId).HasConversion(C.RouteIdConverter).IsRequired();
            record.Property(r => r.RouteName).HasMaxLength(RouteName.MaxLength).IsRequired();
            record.Ignore(r => r.Duration);
            record.Ignore(r => r.AttendanceRate);

            record.HasIndex(r => r.TripId).IsUnique();
            record.HasIndex(r => r.RouteId);
        });
    }

    // ── Outbox / Inbox ───────────────────────────────────────────────────────

    private static void ConfigureMessaging(ModelBuilder builder)
    {
        builder.Entity<OutboxMessage>(message =>
        {
            message.ToTable("AdministrationOutboxMessages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).ValueGeneratedNever();
            message.Property(m => m.MessageType).HasMaxLength(100).IsRequired();
            message.Property(m => m.Payload).HasColumnType("longtext").IsRequired();
            message.Property(m => m.CorrelationId).HasMaxLength(64).IsRequired();
            message.Property(m => m.LastError).HasMaxLength(1000);
            message.HasIndex(m => new { m.ProcessedAt, m.NextAttemptAt });
        });

        builder.Entity<InboxMessage>(message =>
        {
            message.ToTable("AdministrationInboxMessages");
            message.HasKey(m => m.MessageId);
            message.Property(m => m.MessageId).ValueGeneratedNever();
            message.Property(m => m.MessageType).HasMaxLength(100).IsRequired();
        });
    }

    /// <summary>Every DateTimeOffset of this context (including audit columns and complex types) is stored as UTC.</summary>
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
                property.SetValueConverter(C.UtcDateTimeConverter);
                property.SetColumnType("datetime(6)");
            }
        }
    }
}
