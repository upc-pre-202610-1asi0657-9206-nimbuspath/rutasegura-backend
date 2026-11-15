using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Application.IntegrationEvents;
using RutaSegura.TripTracking.Application.Replicas;
using RutaSegura.TripTracking.Application.Tests.Fakes;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Tests.Trips;

[Trait("Category", "Application")]
public class TripInputsTests
{
    [Theory]
    [InlineData("HeavyTraffic", IncidentType.HeavyTraffic)]
    [InlineData("HEAVY_TRAFFIC", IncidentType.HeavyTraffic)]
    [InlineData("student_unwell", IncidentType.StudentUnwell)]
    public void ReportIncident_AcceptsPascalAndUpperSnakeCase(string text, IncidentType expected)
    {
        var command = TripInputs.ReportIncident(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), text, null, false);

        command.Value.Type.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Volcano")]
    [InlineData("3")]
    [InlineData(null)]
    public void ReportIncident_UnknownType_IsValidationError(string? text)
    {
        var command = TripInputs.ReportIncident(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), text, null, false);

        command.FirstError.Code.ShouldBe("VALIDATION_INCIDENT_TYPE");
    }

    [Fact]
    public void OfflineEvent_ParsesTypeAndIncident()
    {
        var input = TripInputs.OfflineEvent(Guid.NewGuid(), "incident", DateTimeOffset.UtcNow, incidentType: "ACCIDENT");

        input.Value.Type.ShouldBe(OfflineEventType.Incident);
        input.Value.IncidentType.ShouldBe(IncidentType.Accident);
    }

    [Fact]
    public void OrganizationTrips_UnknownStatus_IsValidationError()
    {
        var query = TripInputs.OrganizationTrips(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), "Flying");

        query.FirstError.Code.ShouldBe("VALIDATION_TRIP_STATUS");
    }
}

/// <summary>Contrato publicado: cada domain event se traduce a su mensaje versionado sin datos del dominio.</summary>
[Trait("Category", "Application")]
public class TripIntegrationEventMapperTests : TripTrackingTestContext
{
    [Fact]
    public void Map_EveryEventOfAFullTrip_HasAPublishedContract()
    {
        var trip = SeedScheduledTrip(organizationId: SantaRosa);
        var driver = new DriverId(DriverId);
        trip.Start(driver, ClientEventId.New(), ScheduledStart.AddMinutes(15));
        trip.RecordPosition(driver, GpsReading.Create(GeoPosition.Create(-12.1050, -77.0), At(6, 56), 8, 30), At(6, 56));
        trip.RecordPosition(driver, GpsReading.Create(GeoPosition.Create(-12.1001, -77.0), At(6, 58), 8, 10), At(6, 58));
        trip.RecordBoarding(driver, new StudentId(Valeria), ClientEventId.New(), null, null, At(6, 58));
        trip.UndoPassengerRecord(driver, new StudentId(Valeria), ClientEventId.New(), At(6, 58));
        trip.RecordBoarding(driver, new StudentId(Valeria), ClientEventId.New(), null, null, At(6, 58));
        trip.RecordBoarding(driver, new StudentId(Mateo), ClientEventId.New(), null, null, At(6, 58));
        trip.MarkStudentAbsent(driver, new StudentId(Camila), ClientEventId.New(), null, At(7, 0));
        trip.ReportIncident(driver, IncidentType.StudentUnwell, null, true, ClientEventId.New(), null, null, At(7, 1));
        trip.RaiseSos(driver, ClientEventId.New(), null, null, At(7, 2));
        trip.ResolveSos(AdminId, At(7, 3));
        trip.Complete(driver, ClientEventId.New(), At(7, 10));

        var mapped = trip.DomainEvents.Select(e => TripIntegrationEventMapper.Map(trip, e)).ToList();

        mapped.Select(m => m.MessageType).ShouldBe(
        [
            "trip-tracking.trip-started", "trip-tracking.trip-delayed", "trip-tracking.vehicle-approaching-stop",
            "trip-tracking.stop-reached", "trip-tracking.student-boarded", "trip-tracking.passenger-record-undone",
            "trip-tracking.student-boarded", "trip-tracking.student-boarded", "trip-tracking.student-marked-absent",
            "trip-tracking.incident-reported", "trip-tracking.sos-raised", "trip-tracking.sos-resolved", "trip-tracking.trip-completed"
        ]);
        mapped.ShouldAllBe(m => m.SchemaVersion == 1 && m.EventId != Guid.Empty);
        mapped.Select(m => m.EventId).ShouldBe(trip.DomainEvents.Select(e => e.EventId)); // MessageId = EventId (idempotencia)

        var completed = mapped[^1].Payload.ShouldBeOfType<TripCompletedV1>();
        completed.Direction.ShouldBe("Pickup");
        completed.SchoolDeliveredStudentIds.ShouldNotBeNull();
        completed.OrganizationId.ShouldBe(SantaRosa);
        completed.DeliveredStudents.ShouldBe([Valeria, Mateo], ignoreOrder: true);
        completed.AbsentStudents.ShouldBe([Camila]);
    }

    [Fact]
    public void Map_TripWithoutOrganization_PublishesNullOrganization()
    {
        var trip = SeedScheduledTrip();
        trip.Start(new DriverId(DriverId), ClientEventId.New(), ScheduledStart);

        var started = TripIntegrationEventMapper.Map(trip, trip.DomainEvents.Single()).Payload.ShouldBeOfType<TripStartedV1>();

        started.OrganizationId.ShouldBeNull();
        started.ExpectedStudents.Count.ShouldBe(3);
    }
}

[Trait("Category", "Application")]
public class ReplicaCommandTests : TripTrackingTestContext
{
    [Fact]
    public void UpsertRoute_Validator_RejectsUnknownTimeZoneAndStudentsOutsideStops()
    {
        var route = Route3() with
        {
            TimeZoneId = "Mars/Olympus",
            Students = [new RouteStudentSnapshot(Valeria, Guid.NewGuid())]
        };

        var errors = new UpsertRouteSnapshotCommandValidator().Validate(new UpsertRouteSnapshotCommand(route)).Select(e => e.Code).ToList();

        errors.ShouldContain("VALIDATION_ROUTE_TIMEZONE");
        errors.ShouldContain("VALIDATION_ROUTE_STUDENT_STOP");
    }

    [Fact]
    public void UpsertRoute_Validator_AcceptsAValidRoute()
    {
        new UpsertRouteSnapshotCommandValidator().Validate(new UpsertRouteSnapshotCommand(Route3())).ShouldBeEmpty();
    }

    [Fact]
    public async Task LinkGuardian_StoresTheLinkInTheDirectory()
    {
        var store = new InMemoryGuardianStore();

        await new LinkGuardianStudentCommandHandler(store).HandleAsync(new LinkGuardianStudentCommand(UnrelatedGuardian, Camila), Ct);

        store.Links.ShouldContain((new GuardianId(UnrelatedGuardian), new StudentId(Camila)));
    }

    private sealed class InMemoryGuardianStore : IGuardianDirectoryStore
    {
        public HashSet<(GuardianId, StudentId)> Links { get; } = [];

        public Task LinkAsync(GuardianId guardianId, StudentId studentId, CancellationToken cancellationToken)
        {
            Links.Add((guardianId, studentId));
            return Task.CompletedTask;
        }

        public Task UnlinkAsync(GuardianId guardianId, StudentId studentId, CancellationToken cancellationToken)
        {
            Links.Remove((guardianId, studentId));
            return Task.CompletedTask;
        }
    }
}
