using System.Text.Json;
using Monolith.Administration.Application.IntegrationEvents.Inbound;

namespace RutaSegura.Monolith.Administration.Tests.Application;

/// <summary>
///     The JSON below is what Trip &amp; Tracking writes (camelCase, JsonSerializerDefaults.Web). Administration
///     must read it and ignore fields it does not use (tolerant reader).
/// </summary>
public sealed class TripTrackingInboundContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void MessageTypes_AreTheRoutingKeysPublishedByTripTracking() =>
        TripTrackingMessageTypes.Subscribed.ShouldBe(
            ["trip-tracking.incident-reported", "trip-tracking.sos-raised", "trip-tracking.sos-resolved",
                "trip-tracking.trip-completed"], ignoreOrder: true);

    [Fact]
    public void IncidentReported_JsonFromTripTracking_TranslatesToCommand()
    {
        const string body = """
            {"tripId":"7d3c0a7e-62a0-4f43-9c61-0e5bb2c2d6a1","routeId":"3f1a7a5e-0b7d-4c15-8a4e-2b5f9f0d1c11",
             "organizationId":"11111111-1111-1111-1111-111111111111","routeName":"Ruta 3",
             "incidentId":"9b2f6a8c-3f0e-4a4b-8f51-6c7d9e0a1b22","type":"VehicleFailure","detail":"Llanta baja",
             "notifyGuardians":true,"position":{"latitude":-12.11,"longitude":-76.98},
             "affectedStudents":["5b9e4c3a-1d2f-4e6a-9b8c-7d6e5f4a3b2c","6c0f5d4b-2e3a-4f7b-8c9d-0e1f2a3b4c5d"],
             "occurredAt":"2026-10-23T12:40:00+00:00","futureField":"ignored"}
            """;

        var command = JsonSerializer.Deserialize<IncidentReportedV1>(body, Json)!.ToCommand();

        command.IncidentId.ShouldBe(Guid.Parse("9b2f6a8c-3f0e-4a4b-8f51-6c7d9e0a1b22"));
        command.IncidentType.ShouldBe("VehicleFailure");
        command.Latitude.ShouldBe(-12.11);
        command.AffectedStudents.ShouldBe(2);
        command.OrganizationId.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    [Fact]
    public void SosRaised_WithoutPosition_TranslatesToCommandWithoutCoordinates()
    {
        const string body = """
            {"tripId":"7d3c0a7e-62a0-4f43-9c61-0e5bb2c2d6a1","routeId":"3f1a7a5e-0b7d-4c15-8a4e-2b5f9f0d1c11",
             "organizationId":null,"routeName":"Ruta 3","driverId":"aa0e8400-e29b-41d4-a716-446655440000",
             "vehicleId":"bb0e8400-e29b-41d4-a716-446655440000","position":null,"studentsOnBoard":[],
             "occurredAt":"2026-10-23T12:41:00+00:00"}
            """;

        var command = JsonSerializer.Deserialize<SosRaisedV1>(body, Json)!.ToCommand();

        command.Latitude.ShouldBeNull();
        command.OrganizationId.ShouldBeNull();
        command.DriverUserId.ShouldBe(Guid.Parse("aa0e8400-e29b-41d4-a716-446655440000"));
        command.StudentsOnBoard.ShouldBe(0);
    }

    [Fact]
    public void TripCompleted_JsonFromTripTracking_TranslatesCountersToCommand()
    {
        const string body = """
            {"tripId":"7d3c0a7e-62a0-4f43-9c61-0e5bb2c2d6a1","routeId":"3f1a7a5e-0b7d-4c15-8a4e-2b5f9f0d1c11",
             "organizationId":"11111111-1111-1111-1111-111111111111","routeName":"Ruta 3",
             "deliveredStudents":["5b9e4c3a-1d2f-4e6a-9b8c-7d6e5f4a3b2c"],"absentStudents":[],
             "incidentCount":1,"startedAt":"2026-10-23T11:45:00+00:00","completedAt":"2026-10-23T12:50:00+00:00"}
            """;

        var command = JsonSerializer.Deserialize<TripCompletedV1>(body, Json)!.ToCommand();

        command.DeliveredStudents.ShouldBe(1);
        command.AbsentStudents.ShouldBe(0);
        command.IncidentCount.ShouldBe(1);
        (command.CompletedAt - command.StartedAt).ShouldBe(TimeSpan.FromMinutes(65));
    }
}
