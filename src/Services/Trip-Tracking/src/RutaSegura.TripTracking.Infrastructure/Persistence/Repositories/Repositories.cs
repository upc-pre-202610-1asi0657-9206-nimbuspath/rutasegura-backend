using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Infrastructure.Persistence.Records;

namespace RutaSegura.TripTracking.Infrastructure.Persistence.Repositories;

/// <summary>Adaptador EF Core de <see cref="ITripRepository"/>. Siempre carga el agregado completo.</summary>
internal sealed class TripRepository(TripTrackingDbContext db) : ITripRepository
{
    private IQueryable<Trip> Aggregates => db.Trips
        .Include("_stops")
        .Include("_passengers")
        .Include("_incidents")
        .Include("_sosAlerts")
        .AsSplitQuery(); // evita la explosión cartesiana de 4 colecciones

    public Task<Trip?> GetByIdAsync(TripId id, CancellationToken cancellationToken) =>
        Aggregates.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Trip?> FindByRouteAndScheduledStartAsync(RouteId routeId, DateTimeOffset scheduledStart, CancellationToken cancellationToken) =>
        Aggregates.SingleOrDefaultAsync(t => t.RouteId == routeId && t.ScheduledStart == scheduledStart, cancellationToken);

    public async Task<IReadOnlyList<Trip>> ListByDriverAsync(DriverId driverId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        await Aggregates
            .Where(t => t.DriverId == driverId && t.ScheduledStart >= from && t.ScheduledStart < to)
            .OrderBy(t => t.ScheduledStart)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Trip>> ListByStudentsAsync(IReadOnlyCollection<StudentId> students, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (students.Count == 0) return [];

        return await Aggregates
            .Where(t => t.ScheduledStart >= from && t.ScheduledStart < to)
            .Where(HasAnyPassenger(students.Distinct().ToList()))
            .OrderBy(t => t.ScheduledStart)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Trip>> ListByOrganizationAsync(OrganizationId organizationId, DateTimeOffset from, DateTimeOffset to,
        TripStatus? status, CancellationToken cancellationToken)
    {
        var query = Aggregates.Where(t => t.OrganizationId == organizationId && t.ScheduledStart >= from && t.ScheduledStart < to);
        if (status is { } s) query = query.Where(t => t.Status == s);
        return await query.OrderBy(t => t.ScheduledStart).ToListAsync(cancellationToken);
    }

    public void Add(Trip trip) => db.Trips.Add(trip);

    /// <summary>
    /// t => t._passengers.Any(p => p.StudentId == a || p.StudentId == b ...). Se arma a mano porque el proveedor
    /// MySQL de Oracle no tipa colecciones parametrizadas (IN @lista) cuando la columna usa un conversor.
    /// Un tutor tiene pocos hijos: la consulta queda acotada.
    /// </summary>
    private static Expression<Func<Trip, bool>> HasAnyPassenger(IReadOnlyList<StudentId> students)
    {
        var passenger = Expression.Parameter(typeof(TripPassenger), "p");
        var studentId = Expression.Property(passenger, nameof(TripPassenger.StudentId));
        Expression match = students
            .Select(id => (Expression)Expression.Equal(studentId, Expression.Constant(id)))
            .Aggregate(Expression.OrElse);
        var predicate = Expression.Lambda<Func<TripPassenger, bool>>(match, passenger);

        var trip = Expression.Parameter(typeof(Trip), "t");
        var passengers = Expression.Call(typeof(EF), nameof(EF.Property), [typeof(List<TripPassenger>)],
            trip, Expression.Constant("_passengers"));
        var any = Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), [typeof(TripPassenger)], passengers, predicate);
        return Expression.Lambda<Func<Trip, bool>>(any, trip);
    }
}

/// <summary>Telemetría append-only. Se agrega al mismo DbContext: queda en la transacción del comando.</summary>
internal sealed class TrackPointRepository(TripTrackingDbContext db) : ITrackPointRepository
{
    public Task AddRangeAsync(IReadOnlyCollection<TrackPoint> points, CancellationToken cancellationToken)
    {
        db.TrackPoints.AddRange(points.Select(p => new TrackPointRecord
        {
            TripId = p.TripId.Value,
            Latitude = p.Position.Latitude,
            Longitude = p.Position.Longitude,
            RecordedAt = p.RecordedAt,
            ReceivedAt = p.ReceivedAt,
            AccuracyMeters = p.AccuracyMeters,
            SpeedKmh = p.SpeedKmh,
            HeadingDegrees = p.HeadingDegrees
        }));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<TrackPoint>> GetByTripAsync(TripId tripId, CancellationToken cancellationToken)
    {
        var rows = await db.TrackPoints.AsNoTracking()
            .Where(p => p.TripId == tripId.Value)
            .OrderBy(p => p.RecordedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new TrackPoint(tripId, GeoPosition.Create(r.Latitude, r.Longitude), r.RecordedAt, r.ReceivedAt,
            r.AccuracyMeters, r.SpeedKmh, r.HeadingDegrees)).ToList();
    }
}

/// <summary>Réplica local de rutas (lectura + escritura desde consumidores).</summary>
internal sealed class RouteCatalog(TripTrackingDbContext db, TimeProvider clock) : IRouteCatalog, IRouteCatalogStore
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<RouteSnapshot?> GetAsync(RouteId routeId, CancellationToken cancellationToken)
    {
        var row = await db.RouteSnapshots.AsNoTracking().SingleOrDefaultAsync(r => r.RouteId == routeId.Value, cancellationToken);
        return row is null ? null : Deserialize(row);
    }

    public async Task<IReadOnlyList<RouteSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await db.RouteSnapshots.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);
        return rows.Select(Deserialize).ToList();
    }

    public async Task UpsertAsync(RouteSnapshot route, CancellationToken cancellationToken)
    {
        var row = await db.RouteSnapshots.SingleOrDefaultAsync(r => r.RouteId == route.RouteId, cancellationToken);
        if (row is null)
        {
            row = new RouteSnapshotRecord { RouteId = route.RouteId };
            db.RouteSnapshots.Add(row);
        }

        row.Name = route.Name;
        row.OrganizationId = route.OrganizationId == Guid.Empty ? null : route.OrganizationId;
        row.Snapshot = JsonSerializer.Serialize(route, Json);
        row.UpdatedAt = clock.GetUtcNow();
    }

    public async Task RemoveAsync(RouteId routeId, CancellationToken cancellationToken)
    {
        var row = await db.RouteSnapshots.SingleOrDefaultAsync(r => r.RouteId == routeId.Value, cancellationToken);
        if (row is not null) db.RouteSnapshots.Remove(row);
    }

    private static RouteSnapshot Deserialize(RouteSnapshotRecord row) =>
        JsonSerializer.Deserialize<RouteSnapshot>(row.Snapshot, Json)
        ?? throw new InvalidOperationException($"Snapshot de ruta {row.RouteId} corrupto.");
}

/// <summary>Réplica local de vínculos tutor → alumno.</summary>
internal sealed class GuardianDirectory(TripTrackingDbContext db, TimeProvider clock) : IGuardianDirectory, IGuardianDirectoryStore
{
    public async Task<IReadOnlyCollection<StudentId>> GetStudentsOfGuardianAsync(GuardianId guardianId, CancellationToken cancellationToken)
    {
        var ids = await db.GuardianStudents.AsNoTracking()
            .Where(l => l.GuardianId == guardianId.Value)
            .Select(l => l.StudentId)
            .ToListAsync(cancellationToken);
        return ids.Select(id => new StudentId(id)).ToList();
    }

    public async Task LinkAsync(GuardianId guardianId, StudentId studentId, CancellationToken cancellationToken)
    {
        var exists = await db.GuardianStudents.AnyAsync(l => l.GuardianId == guardianId.Value && l.StudentId == studentId.Value, cancellationToken);
        if (!exists)
            db.GuardianStudents.Add(new GuardianStudentLink { GuardianId = guardianId.Value, StudentId = studentId.Value, LinkedAt = clock.GetUtcNow() });
    }

    public async Task UnlinkAsync(GuardianId guardianId, StudentId studentId, CancellationToken cancellationToken)
    {
        var link = await db.GuardianStudents.SingleOrDefaultAsync(l => l.GuardianId == guardianId.Value && l.StudentId == studentId.Value, cancellationToken);
        if (link is not null) db.GuardianStudents.Remove(link);
    }
}
