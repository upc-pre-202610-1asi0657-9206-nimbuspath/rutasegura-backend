using RutaSegura.Notification.Infrastructure.Messaging.Inbound;
using Published = RutaSegura.TripTracking.Application.IntegrationEvents;

namespace RutaSegura.Notification.Messaging.Tests;

public sealed class PublishedContractTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ActualProducerDtosRemainCompatibleWithNotificationAcl(int index)
    {
        var trip = MessageSamples.Trip;
        var route = MessageSamples.Route;
        var org = MessageSamples.Organization;
        var student = MessageSamples.Student;
        var stop = MessageSamples.Stop;
        var now = DateTimeOffset.UtcNow;
        object payload = index switch
        {
            0 => new Published.StudentBoardedV1(trip, route, org, "Ruta", student, stop, new(-12.1, -77), now),
            1 => new Published.StudentAlightedV1(trip, route, org, "Ruta", student, stop, null, now),
            2 => new Published.VehicleApproachingStopV1(trip, route, org, "Ruta", stop, "Parada", 100, 2, [student], now),
            3 => new Published.IncidentReportedV1(trip, route, org, "Ruta", Guid.NewGuid(), "HeavyTraffic", "Detalle que no debe persistir en avisos", true, null, [student], now),
            4 => new Published.SosRaisedV1(trip, route, org, "Ruta", Guid.NewGuid(), Guid.NewGuid(), null, [student], now),
            _ => new Published.TripCompletedV1(trip, route, org, "Ruta", [student], [], 0, now.AddMinutes(-30), now, "Pickup", [student])
        };
        var type = RutaSegura.Notification.Infrastructure.Messaging.RabbitMq.NotificationRabbitMqTopology.RoutingKeys[index];
        var decoded = new TripTrackingMessageDecoder(new()).Decode(MessageSamples.Message(type, payload: payload));
        decoded.Event.OrganizationId.ShouldBe(org);
        decoded.Event.TripId.ShouldBe(trip);
        // Solo tests referencian al productor; las capas productivas siguen desacopladas.
        var internalJson = System.Text.Json.JsonSerializer.Serialize(decoded.Event, decoded.Event.GetType());
        internalJson.ShouldNotContain("Latitude");
        internalJson.ShouldNotContain("Detalle que no debe persistir");
    }
}
