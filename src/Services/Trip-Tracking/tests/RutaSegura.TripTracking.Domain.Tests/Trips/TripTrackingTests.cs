using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tests.Builders;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Domain.Trips.Events;
using static RutaSegura.TripTracking.Domain.Tests.Builders.TripBuilder;

namespace RutaSegura.TripTracking.Domain.Tests.Trips;

/// <summary>Núcleo del Tracking: filtrado de telemetría GPS y detección de paradas (C-12, AC-03).</summary>
[Trait("Category", "Unit")]
public class TripTrackingTests
{
    // Latitudes al sur de la parada 1 (-12.1000). 0.001° ≈ 111 m.
    private const double FarSouth = -12.1200;     // ≈2.2 km de la parada 1
    private const double ApproachingStop1 = -12.1050; // ≈556 m
    private const double AtStop1 = -12.1003;      // ≈33 m

    [Fact]
    public void RecordPosition_ScheduledTrip_ThrowsTrackingRequiresActiveTrip()
    {
        var trip = new TripBuilder().Build();

        var ex = Should.Throw<DomainException>(() => trip.RecordPosition(Driver, Reading(FarSouth, At(6, 30)), At(6, 30)));

        ex.Code.ShouldBe(TrackingErrors.RequiresActiveTrip);
    }

    [Fact]
    public void RecordPosition_CompletedTrip_ThrowsTrackingRequiresActiveTrip()
    {
        var trip = new TripBuilder().BuildStarted();
        foreach (var student in new[] { Valeria, Mateo, Camila })
            trip.MarkStudentAbsent(Driver, student, ClientEventId.New(), null, At(6, 45));
        trip.Complete(Driver, ClientEventId.New(), At(7, 0));

        var ex = Should.Throw<DomainException>(() => trip.RecordPosition(Driver, Reading(FarSouth, At(7, 1)), At(7, 1)));

        ex.Code.ShouldBe(TrackingErrors.RequiresActiveTrip);
    }

    [Fact]
    public void RecordPosition_OtherDriver_ThrowsDriverMismatch()
    {
        var trip = new TripBuilder().BuildStarted();

        Should.Throw<DomainException>(() => trip.RecordPosition(OtherDriver, Reading(FarSouth, At(6, 41)), At(6, 41)))
            .Code.ShouldBe(TripErrors.DriverMismatch);
    }

    [Fact]
    public void RecordPosition_ValidReading_AcceptsAndUpdatesLastPosition()
    {
        var trip = new TripBuilder().BuildStarted();
        var reading = Reading(FarSouth, At(6, 41), speedKmh: 28);

        var result = trip.RecordPosition(Driver, reading, receivedAt: At(6, 41, 2));

        result.Outcome.ShouldBe(PositionRecordingOutcome.Accepted);
        result.Point.ShouldNotBeNull();
        result.Point.TripId.ShouldBe(trip.Id);
        result.Point.ReceivedAt.ShouldBe(At(6, 41, 2));
        trip.LastPosition.ShouldBe(reading.Position);
        trip.LastPositionAt.ShouldBe(At(6, 41));
        trip.LastSpeedKmh.ShouldBe(28);
    }

    [Fact]
    public void RecordPosition_OlderOrEqualThanLastAccepted_IsIgnoredAsStale()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.RecordPosition(Driver, Reading(FarSouth, At(6, 45)), At(6, 45));

        var duplicate = trip.RecordPosition(Driver, Reading(FarSouth, At(6, 45)), At(6, 46));
        var older = trip.RecordPosition(Driver, Reading(FarSouth, At(6, 44)), At(6, 46));

        duplicate.Outcome.ShouldBe(PositionRecordingOutcome.IgnoredStaleOrDuplicate);
        older.Outcome.ShouldBe(PositionRecordingOutcome.IgnoredStaleOrDuplicate);
        trip.LastPositionAt.ShouldBe(At(6, 45));
    }

    [Fact]
    public void RecordPosition_LowAccuracy_IsIgnored()
    {
        var trip = new TripBuilder().BuildStarted();

        var result = trip.RecordPosition(Driver, Reading(FarSouth, At(6, 41), accuracy: 150), At(6, 41));

        result.Outcome.ShouldBe(PositionRecordingOutcome.IgnoredLowAccuracy);
        trip.LastPosition.ShouldBeNull();
    }

    [Fact]
    public void RecordPosition_ImpossibleJump_IsIgnoredAsOutlier()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.RecordPosition(Driver, Reading(FarSouth, At(6, 41)), At(6, 41));

        // 4.4 km en 30 s ≈ 530 km/h
        var result = trip.RecordPosition(Driver, Reading(-12.0800, At(6, 41, 30)), At(6, 41, 30));

        result.Outcome.ShouldBe(PositionRecordingOutcome.IgnoredOutlier);
        trip.LastPosition!.Latitude.ShouldBe(FarSouth);
    }

    [Fact]
    public void RecordPosition_SmallJitterAtHighImpliedSpeed_IsNotAnOutlier()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.RecordPosition(Driver, Reading(FarSouth, At(6, 41, 0)), At(6, 41));

        // 40 m en 1 s = 144 km/h implícitos, pero es jitter normal del GPS
        var result = trip.RecordPosition(Driver, Reading(FarSouth + 0.00036, At(6, 41, 1)), At(6, 41, 1));

        result.Accepted.ShouldBeTrue();
    }

    [Fact]
    public void RecordPosition_TimestampInTheFuture_IsIgnored()
    {
        var trip = new TripBuilder().BuildStarted();

        var result = trip.RecordPosition(Driver, Reading(FarSouth, At(6, 50)), receivedAt: At(6, 45));

        result.Outcome.ShouldBe(PositionRecordingOutcome.IgnoredFutureTimestamp);
    }

    [Fact]
    public void RecordPosition_TimestampBeforeTripStart_IsIgnored()
    {
        var trip = new TripBuilder().BuildStarted(At(6, 40));

        var result = trip.RecordPosition(Driver, Reading(FarSouth, At(6, 30)), At(6, 41));

        result.Outcome.ShouldBe(PositionRecordingOutcome.IgnoredBeforeTripStart);
    }

    [Fact]
    public void RecordPosition_EnteringApproachRadius_RaisesVehicleApproachingStopOnce()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.RecordPosition(Driver, Reading(FarSouth, At(6, 41)), At(6, 41));

        trip.RecordPosition(Driver, Reading(ApproachingStop1, At(6, 44), speedKmh: 30), At(6, 44));
        trip.RecordPosition(Driver, Reading(ApproachingStop1 + 0.0005, At(6, 45), speedKmh: 30), At(6, 45));

        var evt = trip.DomainEvents.OfType<VehicleApproachingStop>().ShouldHaveSingleItem();
        evt.StopId.ShouldBe(Stop1);
        evt.DistanceMeters.ShouldBe(556, tolerance: 2);
        evt.EtaMinutes.ShouldBe(2);
        evt.StudentsAtStop.ShouldBe(new[] { Valeria, Mateo }, ignoreOrder: true);
    }

    [Fact]
    public void RecordPosition_InsideArrivalRadius_MarksStopReachedAndAdvancesNextStop()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.NextStop!.StopId.ShouldBe(Stop1);

        trip.RecordPosition(Driver, Reading(AtStop1, At(6, 50)), At(6, 50));

        var evt = trip.DomainEvents.OfType<StopReached>().ShouldHaveSingleItem();
        evt.StopId.ShouldBe(Stop1);
        evt.OccurredOn.ShouldBe(At(6, 50));
        trip.NextStop!.StopId.ShouldBe(Stop2);
    }

    [Fact]
    public void StudentsExpectedAt_ExcludesStudentsAlreadyBoardedOrAbsent()
    {
        var trip = new TripBuilder().BuildStarted();
        trip.MarkStudentAbsent(Driver, Mateo, ClientEventId.New(), null, At(6, 45));

        trip.StudentsExpectedAt(Stop1).ShouldHaveSingleItem().ShouldBe(Valeria);
    }

    [Fact]
    public void StudentsExpectedAt_ReturnTrip_ListsStudentsOnBoardWhoGetOffThere()
    {
        var trip = new TripBuilder().AsReturnTrip().BuildStarted();
        trip.RecordBoarding(Driver, Valeria, ClientEventId.New(), null, null, At(6, 41));
        trip.RecordBoarding(Driver, Camila, ClientEventId.New(), null, null, At(6, 41));

        trip.StudentsExpectedAt(Stop1).ShouldHaveSingleItem().ShouldBe(Valeria);
    }
}
