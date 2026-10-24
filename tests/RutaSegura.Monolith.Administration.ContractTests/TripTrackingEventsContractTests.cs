using System.Text.Json;
using AdminInbound = Monolith.Administration.Application.IntegrationEvents.Inbound;
using TripEvents = RutaSegura.TripTracking.Application.IntegrationEvents;

namespace RutaSegura.Monolith.Administration.ContractTests;

/// <summary>
///     Trip &amp; Tracking → Administration: the events Trip &amp; Tracking publishes (IncidentReported, SosRaised,
///     SosResolved, TripCompleted) are serialized with its own contract types and read with Administration's.
/// </summary>
public sealed class TripTrackingEventsContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid RouteId = Guid.NewGuid();
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 10, 23, 12, 40, 0, TimeSpan.Zero);

    private static TTarget RoundTrip<TTarget>(object published) =>
        JsonSerializer.Deserialize<TTarget>(JsonSerializer.Serialize(published, published.GetType(), Json), Json)!;

    [Fact]
    public void MessageTypes_AreTheOnesTripTrackingPublishes()
    {
        AdminInbound.TripTrackingMessageTypes.IncidentReported
            .ShouldBe(TripEvents.TripIntegrationEventMapper.Types.IncidentReported);
        AdminInbound.TripTrackingMessageTypes.SosRaised.ShouldBe(TripEvents.TripIntegrationEventMapper.Types.SosRaised);
        AdminInbound.TripTrackingMessageTypes.SosResolved
            .ShouldBe(TripEvents.TripIntegrationEventMapper.Types.SosResolved);
        AdminInbound.TripTrackingMessageTypes.TripCompleted
            .ShouldBe(TripEvents.TripIntegrationEventMapper.Types.TripCompleted);
    }

    [Fact]
    public void IncidentReported_IsReadByAdministration()
    {
        var incidentId = Guid.NewGuid();
        var published = new TripEvents.IncidentReportedV1(TripId, RouteId, OrganizationId, "Ruta 3", incidentId,
            "Accident", "Choque leve", true, new TripEvents.GeoPointV1(-12.11, -76.98), [Guid.NewGuid()], At);

        var command = RoundTrip<AdminInbound.IncidentReportedV1>(published).ToCommand();

        command.TripId.ShouldBe(TripId);
        command.RouteId.ShouldBe(RouteId);
        command.OrganizationId.ShouldBe(OrganizationId);
        command.IncidentId.ShouldBe(incidentId);
        command.IncidentType.ShouldBe("Accident");
        command.Detail.ShouldBe("Choque leve");
        command.Latitude.ShouldBe(-12.11);
        command.Longitude.ShouldBe(-76.98);
        command.AffectedStudents.ShouldBe(1);
        command.OccurredAt.ShouldBe(At);
    }

    [Fact]
    public void SosRaised_IsReadByAdministration()
    {
        var driver = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var published = new TripEvents.SosRaisedV1(TripId, RouteId, null, "Ruta 3", driver, vehicle, null,
            [Guid.NewGuid(), Guid.NewGuid()], At);

        var command = RoundTrip<AdminInbound.SosRaisedV1>(published).ToCommand();

        command.DriverUserId.ShouldBe(driver);
        command.VehicleId.ShouldBe(vehicle);
        command.OrganizationId.ShouldBeNull();
        command.Latitude.ShouldBeNull();
        command.StudentsOnBoard.ShouldBe(2);
    }

    [Fact]
    public void SosResolved_IsReadByAdministration()
    {
        var resolvedBy = Guid.NewGuid();
        var published = new TripEvents.SosResolvedV1(TripId, RouteId, OrganizationId, resolvedBy, At);

        var command = RoundTrip<AdminInbound.SosResolvedV1>(published).ToCommand();

        command.ResolvedBy.ShouldBe(resolvedBy);
        command.TripId.ShouldBe(TripId);
    }

    [Fact]
    public void TripCompleted_IsReadByAdministration()
    {
        var published = new TripEvents.TripCompletedV1(TripId, RouteId, OrganizationId, "Ruta 3",
            [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], [Guid.NewGuid()], 2, At.AddMinutes(-60), At);

        var command = RoundTrip<AdminInbound.TripCompletedV1>(published).ToCommand();

        command.DeliveredStudents.ShouldBe(3);
        command.AbsentStudents.ShouldBe(1);
        command.IncidentCount.ShouldBe(2);
        command.StartedAt.ShouldBe(At.AddMinutes(-60));
        command.CompletedAt.ShouldBe(At);
    }
}
