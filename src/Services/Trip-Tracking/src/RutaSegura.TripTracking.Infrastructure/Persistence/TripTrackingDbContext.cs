using Microsoft.EntityFrameworkCore;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Infrastructure.Persistence.Configurations;
using RutaSegura.TripTracking.Infrastructure.Persistence.Records;

namespace RutaSegura.TripTracking.Infrastructure.Persistence;

/// <summary>
/// Base de datos propia del servicio (Database per Service, PT-02): esquema <c>rutasegura_trip_tracking</c>.
/// La confirmación pasa siempre por <see cref="EfUnitOfWork"/>, que agrega el Outbox en la misma transacción.
/// </summary>
public sealed class TripTrackingDbContext(DbContextOptions<TripTrackingDbContext> options) : DbContext(options)
{
    public DbSet<Trip> Trips => Set<Trip>();
    internal DbSet<TrackPointRecord> TrackPoints => Set<TrackPointRecord>();
    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    internal DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    internal DbSet<RouteSnapshotRecord> RouteSnapshots => Set<RouteSnapshotRecord>();
    internal DbSet<GuardianStudentLink> GuardianStudents => Set<GuardianStudentLink>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        Conventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TripTrackingDbContext).Assembly);
        Conventions.UseSnakeCaseColumns(modelBuilder);
    }
}
