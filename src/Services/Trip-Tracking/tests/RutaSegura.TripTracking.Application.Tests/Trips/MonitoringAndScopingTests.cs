using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Application.Tests.Fakes;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Application.Trips.Queries;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Tests.Trips;

/// <summary>Acceso de la empresa acotado a su organización (claim organizationId) — cierra el TODO de la Etapa 5.</summary>
[Trait("Category", "Application")]
public class OrganizationScopingTests : TripTrackingTestContext
{
    [Fact]
    public async Task LiveStatus_AdminOfAnotherCompany_IsDenied()
    {
        var trip = SeedStartedTrip(organizationId: SantaRosa);

        var result = await new GetTripLiveStatusQueryHandler(Trips, Guardians, Clock)
            .HandleAsync(new GetTripLiveStatusQuery(trip.Id.Value, AdminOf(OtherCompany)), Ct);

        result.FirstError.Code.ShouldBe("TRIP_ACCESS_DENIED");
    }

    [Fact]
    public async Task LiveStatus_AdminOfOwnCompany_SeesAllPassengersAndStops()
    {
        var trip = SeedStartedTrip(organizationId: SantaRosa);

        var result = await new GetTripLiveStatusQueryHandler(Trips, Guardians, Clock)
            .HandleAsync(new GetTripLiveStatusQuery(trip.Id.Value, AdminOf(SantaRosa)), Ct);

        result.Value.Passengers.Count.ShouldBe(3);
        result.Value.Stops.ShouldNotBeNull().Count.ShouldBe(3);
    }

    [Fact]
    public async Task LiveStatus_AdminTokenWithoutOrganization_IsDenied()
    {
        var trip = SeedStartedTrip(organizationId: SantaRosa);

        var result = await new GetTripLiveStatusQueryHandler(Trips, Guardians, Clock)
            .HandleAsync(new GetTripLiveStatusQuery(trip.Id.Value, new Requester(AdminId, RequesterRole.Admin)), Ct);

        result.FirstError.Code.ShouldBe("TRIP_ACCESS_DENIED");
    }

    [Fact]
    public async Task LiveStatus_Guardian_OnlySeesTheStopOfTheirChild()
    {
        var trip = SeedStartedTrip();

        var result = await new GetTripLiveStatusQueryHandler(Trips, Guardians, Clock)
            .HandleAsync(new GetTripLiveStatusQuery(trip.Id.Value, new Requester(GuardianOfValeria, RequesterRole.Guardian)), Ct);

        // Las demás paradas son domicilios de otras familias (AC-01).
        result.Value.Stops.ShouldNotBeNull().ShouldHaveSingleItem().StopId.ShouldBe(Stop1);
    }

    [Fact]
    public async Task CancelTrip_FromAnotherCompany_IsDeniedAndTripStaysScheduled()
    {
        var trip = SeedScheduledTrip(organizationId: SantaRosa);

        var result = await new CancelTripCommandHandler(Trips, Clock)
            .HandleAsync(new CancelTripCommand(trip.Id.Value, "Feriado", OtherCompany), Ct);

        result.FirstError.Code.ShouldBe("TRIP_ACCESS_DENIED");
        trip.Status.ShouldBe(TripStatus.Scheduled);
    }

    [Fact]
    public async Task ResolveSos_FromOwnCompany_ClosesTheAlert()
    {
        var trip = SeedStartedTrip(organizationId: SantaRosa);
        trip.RaiseSos(new DriverId(DriverId), ClientEventId.New(), null, null, At(6, 50));

        var result = await new ResolveSosCommandHandler(Trips, Clock)
            .HandleAsync(new ResolveSosCommand(trip.Id.Value, AdminId, SantaRosa), Ct);

        result.IsSuccess.ShouldBeTrue();
        trip.ActiveSos.ShouldBeNull();
    }

    [Fact]
    public async Task ScheduleTrip_CopiesTheOrganizationOfTheRoute()
    {
        Routes.Routes[RouteId] = Route3(organizationId: SantaRosa);

        var result = await new ScheduleTripCommandHandler(Routes, Trips, Clock)
            .HandleAsync(new ScheduleTripCommand(RouteId, new DateOnly(2026, 10, 5), SantaRosa), Ct);

        Trips.Trips.ShouldHaveSingleItem().OrganizationId.ShouldBe(new OrganizationId(SantaRosa));
        result.Value.ShouldBe(Trips.Trips[0].Id.Value);
    }

    [Fact]
    public async Task ScheduleTrip_RouteOfAnotherCompany_LooksLikeNotFound()
    {
        Routes.Routes[RouteId] = Route3(organizationId: SantaRosa);

        var result = await new ScheduleTripCommandHandler(Routes, Trips, Clock)
            .HandleAsync(new ScheduleTripCommand(RouteId, new DateOnly(2026, 10, 5), OtherCompany), Ct);

        result.FirstError.Code.ShouldBe("ROUTE_NOT_FOUND");
        Trips.Trips.ShouldBeEmpty();
    }
}

[Trait("Category", "Application")]
public class MonitoringQueryTests : TripTrackingTestContext
{
    private static readonly DateTimeOffset DayStart = new(2026, 10, 5, 0, 0, 0, Lima);

    [Fact]
    public async Task GuardianTrips_ReturnsOnlyTripsOfOwnChildrenWithOnlyOwnChildren()
    {
        SeedStartedTrip();

        var result = await new GetGuardianTripsQueryHandler(Trips, Guardians)
            .HandleAsync(new GetGuardianTripsQuery(GuardianOfValeria, DayStart, DayStart.AddDays(1)), Ct);

        var trip = result.Value.ShouldHaveSingleItem();
        trip.IsLive.ShouldBeTrue();
        trip.MyStudents.ShouldHaveSingleItem().StudentId.ShouldBe(Valeria);
    }

    [Fact]
    public async Task GuardianTrips_GuardianWithoutChildren_ReturnsEmpty()
    {
        SeedStartedTrip();

        var result = await new GetGuardianTripsQueryHandler(Trips, Guardians)
            .HandleAsync(new GetGuardianTripsQuery(UnrelatedGuardian, DayStart, DayStart.AddDays(1)), Ct);

        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GuardianTrips_RangeLongerThanAMonth_IsRejected()
    {
        var result = await new GetGuardianTripsQueryHandler(Trips, Guardians)
            .HandleAsync(new GetGuardianTripsQuery(GuardianOfValeria, DayStart, DayStart.AddDays(40)), Ct);

        result.FirstError.Code.ShouldBe("VALIDATION_DATE_RANGE");
    }

    [Fact]
    public async Task OrganizationTrips_FiltersByCompanyAndStatus()
    {
        var mine = SeedStartedTrip(organizationId: SantaRosa);
        SeedScheduledTrip(organizationId: OtherCompany);

        var result = await new GetOrganizationTripsQueryHandler(Trips)
            .HandleAsync(new GetOrganizationTripsQuery(SantaRosa, DayStart, DayStart.AddDays(1), TripStatus.InProgress), Ct);

        var dto = result.Value.ShouldHaveSingleItem();
        dto.TripId.ShouldBe(mine.Id.Value);
        dto.TotalStudents.ShouldBe(3);
    }

    [Fact]
    public async Task OrganizationTrips_WithoutOrganization_IsForbidden()
    {
        var result = await new GetOrganizationTripsQueryHandler(Trips)
            .HandleAsync(new GetOrganizationTripsQuery(Guid.Empty, DayStart, DayStart.AddDays(1)), Ct);

        result.FirstError.Code.ShouldBe("ORGANIZATION_REQUIRED");
    }

    [Fact]
    public async Task TripTrack_Driver_GetsAcceptedPointsInOrder()
    {
        var trip = SeedStartedTrip();
        await TrackPoints.AddRangeAsync(
        [
            new TrackPoint(trip.Id, GeoPosition.Create(-12.1040, -77.0), At(6, 51), At(6, 51), 6, 20, null),
            new TrackPoint(trip.Id, GeoPosition.Create(-12.1050, -77.0), At(6, 50), At(6, 50), 8, 25, null)
        ], Ct);

        var result = await new GetTripTrackQueryHandler(Trips, TrackPoints)
            .HandleAsync(new GetTripTrackQuery(trip.Id.Value, new Requester(DriverId, RequesterRole.Driver)), Ct);

        result.Value.PointCount.ShouldBe(2);
        result.Value.Points[0].RecordedAt.ShouldBe(At(6, 50));
    }

    [Fact]
    public async Task TripTrack_Guardian_IsDenied()
    {
        var trip = SeedStartedTrip();

        var result = await new GetTripTrackQueryHandler(Trips, TrackPoints)
            .HandleAsync(new GetTripTrackQuery(trip.Id.Value, new Requester(GuardianOfValeria, RequesterRole.Guardian)), Ct);

        result.FirstError.Code.ShouldBe("TRIP_ACCESS_DENIED");
    }

    [Fact]
    public async Task TripPosition_PrefersFresherCachedPosition()
    {
        var trip = SeedStartedTrip();
        trip.RecordPosition(new DriverId(DriverId), GpsReading.Create(GeoPosition.Create(-12.1050, -77.0), At(6, 50), 8, 30), At(6, 50));
        await LastPositions.SetAsync(new LivePositionUpdate(trip.Id.Value, RouteId, VehicleId, "InProgress",
            -12.1040, -77.0, At(6, 50, 30), 18, null, Stop1, 445, 2, 0), Ct);
        Clock.SetUtcNow(At(6, 50, 40));

        var result = await new GetTripPositionQueryHandler(Trips, Guardians, LastPositions, Clock)
            .HandleAsync(new GetTripPositionQuery(trip.Id.Value, new Requester(GuardianOfValeria, RequesterRole.Guardian)), Ct);

        var position = result.Value.Position.ShouldNotBeNull();
        position.Latitude.ShouldBe(-12.1040);
        position.AgeSeconds.ShouldBe(10);
        result.Value.NextStopEtaMinutes.ShouldBe(2);
    }

    [Fact]
    public async Task TripPosition_WithoutCache_FallsBackToTheAggregate()
    {
        var trip = SeedStartedTrip();
        trip.RecordPosition(new DriverId(DriverId), GpsReading.Create(GeoPosition.Create(-12.1050, -77.0), At(6, 50), 8, 30), At(6, 50));

        var result = await new GetTripPositionQueryHandler(Trips, Guardians, LastPositions, Clock)
            .HandleAsync(new GetTripPositionQuery(trip.Id.Value, new Requester(DriverId, RequesterRole.Driver)), Ct);

        result.Value.Position.ShouldNotBeNull().Latitude.ShouldBe(-12.1050);
        result.Value.NextStopId.ShouldBe(Stop1);
    }

    [Fact]
    public async Task TripPosition_UnrelatedGuardian_IsDenied()
    {
        var trip = SeedStartedTrip();

        var result = await new GetTripPositionQueryHandler(Trips, Guardians, LastPositions, Clock)
            .HandleAsync(new GetTripPositionQuery(trip.Id.Value, new Requester(UnrelatedGuardian, RequesterRole.Guardian)), Ct);

        result.FirstError.Code.ShouldBe("TRIP_ACCESS_DENIED");
    }

    [Fact]
    public async Task TripPosition_ScheduledTrip_HidesPosition()
    {
        var trip = SeedScheduledTrip();

        var result = await new GetTripPositionQueryHandler(Trips, Guardians, LastPositions, Clock)
            .HandleAsync(new GetTripPositionQuery(trip.Id.Value, new Requester(GuardianOfValeria, RequesterRole.Guardian)), Ct);

        result.Value.Position.ShouldBeNull();
        result.Value.Status.ShouldBe("Scheduled");
    }
}
