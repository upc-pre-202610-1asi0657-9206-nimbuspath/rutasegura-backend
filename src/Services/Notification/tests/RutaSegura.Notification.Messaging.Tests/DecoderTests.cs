using System.Text;
using RutaSegura.Notification.Application.Contracts.V1;
using RutaSegura.Notification.Infrastructure.Messaging.Inbound;
using RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;

namespace RutaSegura.Notification.Messaging.Tests;

public sealed class DecoderTests
{
    private readonly TripTrackingMessageDecoder _decoder = new(new());

    [Theory]
    [InlineData("trip-tracking.student-boarded", typeof(StudentBoardedV1))]
    [InlineData("trip-tracking.student-alighted", typeof(StudentAlightedV1))]
    [InlineData("trip-tracking.vehicle-approaching-stop", typeof(VehicleApproachingStopV1))]
    [InlineData("trip-tracking.incident-reported", typeof(IncidentReportedV1))]
    [InlineData("trip-tracking.sos-raised", typeof(SosRaisedV1))]
    [InlineData("trip-tracking.trip-completed", typeof(TripCompletedV1))]
    public void MapsActualPublishedLanguageToStableInternalContracts(string wireType, Type expected)
    {
        var input = MessageSamples.Message(wireType);
        var result = _decoder.Decode(input);
        result.Event.GetType().ShouldBe(expected);
        result.Metadata.MessageId.ShouldBe(Guid.Parse(input.MessageId!));
        result.Metadata.MessageType.ShouldBe(TripNotificationContracts.MessageType(result.Event));
        result.Event.OrganizationId.ShouldBe(MessageSamples.Organization);
    }

    [Theory]
    [InlineData("schema-version", 2)]
    [InlineData("message-type", "unknown")]
    [InlineData("occurred-at", "invalid")]
    [InlineData("correlation-id", "changed")]
    public void RejectsChangedImmutableMetadata(string header, object value)
    {
        var message = MessageSamples.Message();
        message.Headers![header] = value;
        Should.Throw<InvalidNotificationMessageException>(() => _decoder.Decode(message));
    }

    [Fact]
    public void RejectsMissingTenantInsteadOfInferringItFromRecipients()
    {
        var input = MessageSamples.Message(payload: new { tripId = MessageSamples.Trip, studentId = MessageSamples.Student, stopId = MessageSamples.Stop });
        Should.Throw<InvalidNotificationMessageException>(() => _decoder.Decode(input));
    }

    [Fact]
    public void RejectsLegacyCompletionWithoutDirectionAndNewlyDeliveredSubset()
    {
        var input = MessageSamples.Message("trip-tracking.trip-completed", payload: new { organizationId = MessageSamples.Organization,
            tripId = MessageSamples.Trip, routeId = MessageSamples.Route, deliveredStudents = new[] { MessageSamples.Student } });
        Should.Throw<InvalidNotificationMessageException>(() => _decoder.Decode(input));
    }

    [Fact]
    public void RetryRoutingPreservesOriginalTypeAndCanonicalIdentity()
    {
        var input = MessageSamples.Message();
        input.Headers![TripTrackingMessageDecoder.OriginalRoutingHeader] = Encoding.UTF8.GetBytes(input.RoutingKey);
        input.Headers[TripTrackingMessageDecoder.RetryHeader] = 1;
        var retried = input with { RoutingKey = new NotificationRabbitMqOptions().Queue };
        _decoder.Decode(retried).ShouldBe(_decoder.Decode(input));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65537)]
    public void RejectsEmptyAndOversizedBodies(int bytes) =>
        Should.Throw<InvalidNotificationMessageException>(() => _decoder.Decode(MessageSamples.Message() with { Body = new byte[bytes] }));

    [Fact]
    public void RejectsMalformedJsonAndUnknownRoutes()
    {
        Should.Throw<InvalidNotificationMessageException>(() => _decoder.Decode(MessageSamples.Message() with { Body = "{"u8.ToArray() }));
        Should.Throw<InvalidNotificationMessageException>(() => _decoder.Decode(MessageSamples.Message() with { RoutingKey = "trip-tracking.sos-raised" }));
    }
}
