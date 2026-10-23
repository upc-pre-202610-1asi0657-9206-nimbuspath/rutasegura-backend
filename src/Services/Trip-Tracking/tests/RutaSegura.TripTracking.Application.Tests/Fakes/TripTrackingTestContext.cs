using Microsoft.Extensions.Time.Testing;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Tests.Fakes;

/// <summary>
/// Fixture por prueba (xUnit crea una instancia nueva por test): fakes de todos los puertos,
/// reloj controlable fijado en "lunes 5 de octubre de 2026, 06:40 (Lima)" y datos de la Ruta 3.
/// </summary>
public abstract class TripTrackingTestContext
{
    protected static readonly TimeSpan Lima = TimeSpan.FromHours(-5);
    protected static readonly DateTimeOffset ScheduledStart = new(2026, 10, 5, 6, 40, 0, Lima);

    protected static readonly Guid DriverId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    protected static readonly Guid OtherDriverId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    protected static readonly Guid VehicleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    protected static readonly Guid RouteId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    protected static readonly Guid Stop1 = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    protected static readonly Guid Stop2 = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    protected static readonly Guid School = Guid.Parse("a0000000-0000-0000-0000-000000000003");

    protected static readonly Guid Valeria = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    protected static readonly Guid Mateo = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    protected static readonly Guid Camila = Guid.Parse("b0000000-0000-0000-0000-000000000003");

    protected static readonly Guid GuardianOfValeria = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    protected static readonly Guid UnrelatedGuardian = Guid.Parse("c0000000-0000-0000-0000-000000000099");

    protected static readonly Guid SantaRosa = Guid.Parse("0a000000-0000-0000-0000-000000000001");
    protected static readonly Guid OtherCompany = Guid.Parse("0b000000-0000-0000-0000-000000000002");
    protected static readonly Guid AdminId = Guid.Parse("0a000000-0000-0000-0000-0000000000ad");

    internal InMemoryTripRepository Trips { get; } = new();
    internal InMemoryTrackPointRepository TrackPoints { get; } = new();
    internal RecordingLivePositionPublisher LivePublisher { get; } = new();
    internal FakeGuardianDirectory Guardians { get; } = new FakeGuardianDirectory()
        .Link(new GuardianId(GuardianOfValeria), new StudentId(Valeria));
    internal FakeRouteCatalog Routes { get; } = new();
    internal InMemoryLastPositionCache LastPositions { get; } = new();
    protected FakeTimeProvider Clock { get; } = new(ScheduledStart);

    protected static DateTimeOffset At(int hour, int minute, int second = 0) => new(2026, 10, 5, hour, minute, second, Lima);

    protected static Requester AdminOf(Guid organizationId) => new(AdminId, RequesterRole.Admin, organizationId);

    protected static RouteSnapshot Route3(TripDirection direction = TripDirection.Pickup, Guid organizationId = default) => new(
        RouteId, "Ruta 3", direction, DriverId, VehicleId, new TimeOnly(6, 40), "America/Lima",
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        [
            new RouteStopSnapshot(Stop1, 1, "Av. Primavera 1240", -12.1000, -77.0),
            new RouteStopSnapshot(Stop2, 2, "Jr. Las Begonias 310", -12.0900, -77.0),
            new RouteStopSnapshot(School, 3, "Colegio San Agustín", -12.0800, -77.0)
        ],
        [
            new RouteStudentSnapshot(Valeria, Stop1),
            new RouteStudentSnapshot(Mateo, Stop1),
            new RouteStudentSnapshot(Camila, Stop2)
        ],
        organizationId);

    /// <summary>Guarda en el repositorio un viaje programado (sin eventos pendientes).</summary>
    protected Trip SeedScheduledTrip(TripDirection direction = TripDirection.Pickup, Guid? organizationId = null)
    {
        var route = Route3(direction);
        var stops = route.Stops.Select(s => new TripStopDefinition(new StopId(s.StopId),
            direction == TripDirection.Pickup ? s.Sequence : 4 - s.Sequence, s.Name, GeoPosition.Create(s.Latitude, s.Longitude))).ToList();
        var trip = Trip.Schedule(TripId.New(), new RouteId(RouteId), route.Name, direction,
            new DriverId(DriverId), new VehicleId(VehicleId), ScheduledStart, stops,
            route.Students.Select(s => new TripPassengerDefinition(new StudentId(s.StudentId), new StopId(s.StopId))).ToList(),
            ScheduledStart.AddHours(-12),
            new OrganizationId(organizationId ?? Guid.Empty));
        trip.ClearDomainEvents();
        Trips.Add(trip);
        return trip;
    }

    /// <summary>Guarda un viaje ya iniciado a las 06:40 (sin eventos pendientes).</summary>
    protected Trip SeedStartedTrip(TripDirection direction = TripDirection.Pickup, Guid? organizationId = null)
    {
        var trip = SeedScheduledTrip(direction, organizationId);
        trip.Start(new DriverId(DriverId), ClientEventId.New(), ScheduledStart);
        trip.ClearDomainEvents();
        return trip;
    }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;
}
