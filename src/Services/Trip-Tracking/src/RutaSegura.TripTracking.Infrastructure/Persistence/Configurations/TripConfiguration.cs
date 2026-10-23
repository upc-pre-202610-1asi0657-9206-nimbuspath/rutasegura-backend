using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo del agregado <see cref="Trip"/> sin tocar el dominio: colecciones privadas por backing field,
/// <c>GeoPosition</c> como complex type (value object sin identidad) y versión de concurrencia en sombra.
/// </summary>
internal sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public const string VersionProperty = "Version";
    public const string TripForeignKey = "TripId";

    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("trips");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.RouteId).IsRequired();
        builder.Property(t => t.OrganizationId).IsRequired();
        builder.Property(t => t.RouteName).HasMaxLength(120).IsRequired();
        builder.Property(t => t.Direction).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.CancellationReason).HasMaxLength(250);

        builder.ComplexProperty(t => t.LastPosition, position =>
        {
            position.Property(p => p.Latitude).HasColumnName("last_latitude");
            position.Property(p => p.Longitude).HasColumnName("last_longitude");
        });

        // Concurrencia optimista a nivel de agregado: la Unit of Work incrementa la versión cuando
        // cambia el viaje o cualquiera de sus partes (paradas, pasajeros, incidentes, SOS).
        builder.Property<int>(VersionProperty).IsConcurrencyToken();

        builder.Property<HashSet<ClientEventId>>("_processedClientEvents")
            .HasColumnName("processed_client_events")
            .HasColumnType("longtext")
            .HasConversion(ProcessedClientEventsMapping.Converter, ProcessedClientEventsMapping.Comparer)
            .IsRequired();

        builder.Ignore(t => t.Stops);
        builder.Ignore(t => t.Passengers);
        builder.Ignore(t => t.Incidents);
        builder.Ignore(t => t.SosAlerts);
        builder.Ignore(t => t.ProcessedClientEvents);
        builder.Ignore(t => t.ActiveSos);
        builder.Ignore(t => t.NextStop);
        builder.Ignore(t => t.PassengersOnBoard);
        builder.Ignore(t => t.IsActive);
        builder.Ignore(t => t.DomainEvents);

        builder.HasMany<TripStop>("_stops").WithOne().HasForeignKey(TripForeignKey).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<TripPassenger>("_passengers").WithOne().HasForeignKey(TripForeignKey).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<Incident>("_incidents").WithOne().HasForeignKey(TripForeignKey).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<SosAlert>("_sosAlerts").WithOne().HasForeignKey(TripForeignKey).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation("_stops").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation("_passengers").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation("_incidents").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation("_sosAlerts").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Programar es idempotente: una ruta no puede tener dos viajes a la misma hora (protege la carrera entre réplicas).
        builder.HasIndex(t => new { t.RouteId, t.ScheduledStart }).IsUnique();
        builder.HasIndex(t => new { t.DriverId, t.ScheduledStart });
        builder.HasIndex(t => new { t.OrganizationId, t.ScheduledStart });
        builder.HasIndex(t => t.Status);
    }
}

internal sealed class TripStopConfiguration : IEntityTypeConfiguration<TripStop>
{
    public void Configure(EntityTypeBuilder<TripStop> builder)
    {
        builder.ToTable("trip_stops");
        builder.Property<TripId>(TripConfiguration.TripForeignKey);
        builder.HasKey(TripConfiguration.TripForeignKey, nameof(TripStop.StopId));
        builder.Property(s => s.StopId).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(160).IsRequired();
        builder.ComplexProperty(s => s.Position, position =>
        {
            position.IsRequired();
            position.Property(p => p.Latitude).HasColumnName("latitude");
            position.Property(p => p.Longitude).HasColumnName("longitude");
        });
        builder.Ignore(s => s.IsReached);
    }
}

internal sealed class TripPassengerConfiguration : IEntityTypeConfiguration<TripPassenger>
{
    public void Configure(EntityTypeBuilder<TripPassenger> builder)
    {
        builder.ToTable("trip_passengers");
        builder.Property<TripId>(TripConfiguration.TripForeignKey);
        builder.HasKey(TripConfiguration.TripForeignKey, nameof(TripPassenger.StudentId));
        builder.Property(p => p.StudentId).ValueGeneratedNever();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.AbsenceSource).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.AbsenceNote).HasMaxLength(250);
        builder.ComplexProperty(p => p.BoardedPosition, position =>
        {
            position.Property(p => p.Latitude).HasColumnName("boarded_latitude");
            position.Property(p => p.Longitude).HasColumnName("boarded_longitude");
        });
        builder.ComplexProperty(p => p.AlightedPosition, position =>
        {
            position.Property(p => p.Latitude).HasColumnName("alighted_latitude");
            position.Property(p => p.Longitude).HasColumnName("alighted_longitude");
        });

        // Pantalla de inicio del tutor: "¿en qué viajes va mi hijo?"
        builder.HasIndex(p => p.StudentId);
    }
}

internal sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("trip_incidents");
        builder.Property<TripId>(TripConfiguration.TripForeignKey);
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.Detail).HasMaxLength(Incident.MaxDetailLength);
        builder.ComplexProperty(i => i.Position, position =>
        {
            position.Property(p => p.Latitude).HasColumnName("latitude");
            position.Property(p => p.Longitude).HasColumnName("longitude");
        });
    }
}

internal sealed class SosAlertConfiguration : IEntityTypeConfiguration<SosAlert>
{
    public void Configure(EntityTypeBuilder<SosAlert> builder)
    {
        builder.ToTable("trip_sos_alerts");
        builder.Property<TripId>(TripConfiguration.TripForeignKey);
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.ComplexProperty(s => s.Position, position =>
        {
            position.Property(p => p.Latitude).HasColumnName("latitude");
            position.Property(p => p.Longitude).HasColumnName("longitude");
        });
        builder.Ignore(s => s.IsActive);
    }
}
