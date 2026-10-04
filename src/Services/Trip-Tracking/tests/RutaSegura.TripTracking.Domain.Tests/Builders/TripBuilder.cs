using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Domain.Tests.Builders;

/// <summary>
/// Test Data Builder + Object Mother: "Ruta 3, recojo de la mañana" del prototipo del conductor.
/// Tres paradas en línea recta (≈1.1 km entre cada una) para que las distancias sean predecibles.
/// </summary>
public sealed class TripBuilder
{
    public static readonly DateTimeOffset ScheduledStart = new(2026, 10, 5, 6, 40, 0, TimeSpan.FromHours(-5));

    public static readonly DriverId Driver = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    public static readonly DriverId OtherDriver = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    public static readonly VehicleId Vehicle = new(Guid.Parse("33333333-3333-3333-3333-333333333333"));
    public static readonly RouteId Route = new(Guid.Parse("44444444-4444-4444-4444-444444444444"));

    public static readonly StopId Stop1 = new(Guid.Parse("a0000000-0000-0000-0000-000000000001"));
    public static readonly StopId Stop2 = new(Guid.Parse("a0000000-0000-0000-0000-000000000002"));
    public static readonly StopId School = new(Guid.Parse("a0000000-0000-0000-0000-000000000003"));

    public static readonly GeoPosition Stop1Position = GeoPosition.Create(-12.1000, -77.0000);
    public static readonly GeoPosition Stop2Position = GeoPosition.Create(-12.0900, -77.0000);
    public static readonly GeoPosition SchoolPosition = GeoPosition.Create(-12.0800, -77.0000);

    public static readonly StudentId Valeria = new(Guid.Parse("b0000000-0000-0000-0000-000000000001"));
    public static readonly StudentId Mateo = new(Guid.Parse("b0000000-0000-0000-0000-000000000002"));
    public static readonly StudentId Camila = new(Guid.Parse("b0000000-0000-0000-0000-000000000003"));
    public static readonly StudentId Stranger = new(Guid.Parse("b0000000-0000-0000-0000-000000000099"));

    private TripDirection _direction = TripDirection.Pickup;
    private TripId _id = TripId.New();

    public TripBuilder WithId(TripId id)
    {
        _id = id;
        return this;
    }

    public TripBuilder AsReturnTrip()
    {
        _direction = TripDirection.Return;
        return this;
    }

    /// <summary>Viaje programado, sin eventos pendientes.</summary>
    public Trip Build()
    {
        var stops = _direction == TripDirection.Pickup
            ? new[]
            {
                new TripStopDefinition(Stop1, 1, "Av. Primavera 1240", Stop1Position),
                new TripStopDefinition(Stop2, 2, "Jr. Las Begonias 310", Stop2Position),
                new TripStopDefinition(School, 3, "Colegio San Agustín", SchoolPosition)
            }
            : new[]
            {
                new TripStopDefinition(School, 1, "Colegio San Agustín", SchoolPosition),
                new TripStopDefinition(Stop2, 2, "Jr. Las Begonias 310", Stop2Position),
                new TripStopDefinition(Stop1, 3, "Av. Primavera 1240", Stop1Position)
            };

        var passengers = new[]
        {
            new TripPassengerDefinition(Valeria, Stop1),
            new TripPassengerDefinition(Mateo, Stop1),
            new TripPassengerDefinition(Camila, Stop2)
        };

        var trip = Trip.Schedule(_id, Route, "Ruta 3", _direction, Driver, Vehicle,
            ScheduledStart, stops, passengers, ScheduledStart.AddHours(-12));
        trip.ClearDomainEvents();
        return trip;
    }

    /// <summary>Viaje iniciado a la hora exacta (06:40), sin eventos pendientes.</summary>
    public Trip BuildStarted(DateTimeOffset? startedAt = null)
    {
        var trip = Build();
        trip.Start(Driver, ClientEventId.New(), startedAt ?? ScheduledStart);
        trip.ClearDomainEvents();
        return trip;
    }

    public static GpsReading Reading(double latitude, DateTimeOffset at, double accuracy = 8, double? speedKmh = 30) =>
        GpsReading.Create(GeoPosition.Create(latitude, -77.0000), at, accuracy, speedKmh);

    public static DateTimeOffset At(int hour, int minute, int second = 0) =>
        new(2026, 10, 5, hour, minute, second, TimeSpan.FromHours(-5));
}
