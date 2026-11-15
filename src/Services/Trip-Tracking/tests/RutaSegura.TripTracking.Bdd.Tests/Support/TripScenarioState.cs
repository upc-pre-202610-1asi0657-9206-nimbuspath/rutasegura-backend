using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Bdd.Tests.Support;

public sealed class TripScenarioState
{
    public const double FarSouth = -12.1200;

    public static readonly DateTimeOffset ScheduledStart =
        new(2026, 10, 5, 6, 40, 0, TimeSpan.FromHours(-5));

    public static readonly DriverId Driver =
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    public static readonly DriverId OtherDriver =
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    public static readonly VehicleId Vehicle =
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"));

    public static readonly RouteId Route =
        new(Guid.Parse("44444444-4444-4444-4444-444444444444"));

    public static readonly StopId Stop1 =
        new(Guid.Parse("a0000000-0000-0000-0000-000000000001"));

    public static readonly StopId Stop2 =
        new(Guid.Parse("a0000000-0000-0000-0000-000000000002"));

    public static readonly StopId School =
        new(Guid.Parse("a0000000-0000-0000-0000-000000000003"));

    public static readonly StudentId Valeria =
        new(Guid.Parse("b0000000-0000-0000-0000-000000000001"));

    public static readonly StudentId Mateo =
        new(Guid.Parse("b0000000-0000-0000-0000-000000000002"));

    public static readonly StudentId Stranger =
        new(Guid.Parse("b0000000-0000-0000-0000-000000000099"));

    public static readonly StudentId Camila =
        new(Guid.Parse("b0000000-0000-0000-0000-000000000003"));

    public Trip Trip { get; set; } = null!;

    public bool? OperationApplied { get; set; }

    public string? ErrorCode { get; set; }

    public ClientEventId ClientEventId { get; set; }

    public DateTimeOffset ActionTime { get; set; }

    public GpsReading? CurrentReading { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public PositionRecordingResult? PositionResult { get; set; }

    public GeoPosition? PreviousPosition { get; set; }

    public DateTimeOffset? PreviousPositionAt { get; set; }

    public StudentId CurrentStudent { get; set; }

    public GeoPosition? EventPosition { get; set; }

    public Func<bool>? RepeatOperation { get; set; }

    public static Trip CreateScheduledTrip()
    {
        var stops = new[]
        {
            new TripStopDefinition(
                Stop1,
                1,
                "Av. Primavera 1240",
                GeoPosition.Create(-12.1000, -77.0000)),

            new TripStopDefinition(
                Stop2,
                2,
                "Jr. Las Begonias 310",
                GeoPosition.Create(-12.0900, -77.0000)),

            new TripStopDefinition(
                School,
                3,
                "Colegio San Agustín",
                GeoPosition.Create(-12.0800, -77.0000))
        };

        var passengers = new[]
        {
            new TripPassengerDefinition(Valeria, Stop1),
            new TripPassengerDefinition(Mateo, Stop1),
            new TripPassengerDefinition(Camila, Stop2)
        };

        var trip = Trip.Schedule(
            TripId.New(),
            Route,
            "Ruta 3",
            TripDirection.Pickup,
            Driver,
            Vehicle,
            ScheduledStart,
            stops,
            passengers,
            ScheduledStart.AddHours(-12));

        trip.ClearDomainEvents();

        return trip;
    }

    public static Trip CreateStartedTrip()
    {
        var trip = CreateScheduledTrip();

        trip.Start(
            Driver,
            ClientEventId.New(),
            ScheduledStart);

        trip.ClearDomainEvents();

        return trip;
    }

    public static Trip CreateStartedReturnTrip()
    {
        var stops = new[]
        {
            new TripStopDefinition(
                School,
                1,
                "Colegio San Agustín",
                GeoPosition.Create(-12.0800, -77.0000)),

            new TripStopDefinition(
                Stop2,
                2,
                "Jr. Las Begonias 310",
                GeoPosition.Create(-12.0900, -77.0000)),

            new TripStopDefinition(
                Stop1,
                3,
                "Av. Primavera 1240",
                GeoPosition.Create(-12.1000, -77.0000))
        };

        var passengers = new[]
        {
            new TripPassengerDefinition(Valeria, Stop1),
            new TripPassengerDefinition(Mateo, Stop1),
            new TripPassengerDefinition(Camila, Stop2)
        };

        var trip = Trip.Schedule(
            TripId.New(),
            Route,
            "Ruta 3",
            TripDirection.Return,
            Driver,
            Vehicle,
            ScheduledStart,
            stops,
            passengers,
            ScheduledStart.AddHours(-12));

        trip.ClearDomainEvents();

        trip.Start(
            Driver,
            ClientEventId.New(),
            ScheduledStart);

        trip.ClearDomainEvents();

        return trip;
    }

    public static GpsReading CreateReading(
        double latitude,
        DateTimeOffset recordedAt,
        double accuracy = 8,
        double? speedKmh = 30)
    {
        return GpsReading.Create(
            GeoPosition.Create(latitude, -77.0000),
            recordedAt,
            accuracy,
            speedKmh);
    }

    public static DateTimeOffset At(
        int hour,
        int minute,
        int second = 0)
    {
        return new DateTimeOffset(
            2026,
            10,
            5,
            hour,
            minute,
            second,
            TimeSpan.FromHours(-5));
    }
}