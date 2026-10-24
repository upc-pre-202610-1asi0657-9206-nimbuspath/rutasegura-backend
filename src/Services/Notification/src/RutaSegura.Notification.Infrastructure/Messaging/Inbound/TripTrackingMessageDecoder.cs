using System.Globalization;
using System.Text;
using System.Text.Json;
using RutaSegura.Notification.Application.Contracts.V1;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;

namespace RutaSegura.Notification.Infrastructure.Messaging.Inbound;

/// <summary>ACL del Published Language actual de Tracking hacia los contratos internos de Notification.</summary>
public sealed class TripTrackingMessageDecoder(NotificationRabbitMqOptions options)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public const string RetryHeader = "x-notification-retry";
    public const string OriginalRoutingHeader = "x-notification-original-routing-key";

    public ReceiveTripEventCommand Decode(NotificationIncomingMessage message)
    {
        try
        {
            if (message.Body.Length == 0 || message.Body.Length > options.MaxMessageBytes)
                throw Invalid("NOTIFICATION_MESSAGE_SIZE");
            if (message.ContentType != "application/json" || !Guid.TryParse(message.MessageId, out var id) || id == Guid.Empty)
                throw Invalid("NOTIFICATION_INVALID_ENVELOPE");
            var type = ReadHeader(message.Headers, "message-type");
            if (type != message.MessageType || !NotificationRabbitMqTopology.RoutingKeys.Contains(type))
                throw Invalid("NOTIFICATION_UNKNOWN_MESSAGE_TYPE");
            var originalRouting = message.RoutingKey == options.Queue ? ReadHeader(message.Headers, OriginalRoutingHeader) : message.RoutingKey;
            if (originalRouting != type || ReadHeader(message.Headers, "schema-version") != "1")
                throw Invalid("NOTIFICATION_UNSUPPORTED_WIRE_CONTRACT");
            if (!DateTimeOffset.TryParse(ReadHeader(message.Headers, "occurred-at"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var occurredAt) || occurredAt == default ||
                (message.UnixTimestamp != 0 && message.UnixTimestamp != occurredAt.ToUnixTimeSeconds()))
                throw Invalid("NOTIFICATION_INVALID_OCCURRED_AT");
            var correlation = ReadHeader(message.Headers, "correlation-id");
            if (string.IsNullOrWhiteSpace(correlation) || correlation.Length > 200 || correlation != message.CorrelationId)
                throw Invalid("NOTIFICATION_INVALID_CORRELATION");
            using var json = JsonDocument.Parse(message.Body, new JsonDocumentOptions { MaxDepth = 16 });
            var payload = json.RootElement;
            var org = Id(payload, "organizationId");
            var trip = Id(payload, "tripId");
            TripNotificationEventV1 translated = type switch
            {
                "trip-tracking.student-boarded" => new StudentBoardedV1(org, trip, Id(payload, "studentId"), Id(payload, "stopId")),
                "trip-tracking.student-alighted" => new StudentAlightedV1(org, trip, Id(payload, "studentId"), Id(payload, "stopId")),
                "trip-tracking.vehicle-approaching-stop" => new VehicleApproachingStopV1(org, trip, Id(payload, "stopId"),
                    Ids(payload, "studentsAtStop"), payload.GetProperty("etaMinutes").GetInt32()),
                "trip-tracking.incident-reported" => new IncidentReportedV1(org, trip, Id(payload, "incidentId"),
                    payload.GetProperty("notifyGuardians").GetBoolean(), Ids(payload, "affectedStudents")),
                "trip-tracking.sos-raised" => new SosRaisedV1(org, trip, Id(payload, "driverId"), Id(payload, "vehicleId"), Ids(payload, "studentsOnBoard")),
                "trip-tracking.trip-completed" => new TripCompletedV1(org, trip, Id(payload, "routeId"),
                    payload.GetProperty("direction").GetString() switch
                    {
                        "Pickup" => TripDirectionV1.Pickup, "Return" => TripDirectionV1.Return,
                        _ => throw Invalid("NOTIFICATION_COMPLETION_DIRECTION_REQUIRED")
                    }, Ids(payload, "schoolDeliveredStudentIds")),
                _ => throw Invalid("NOTIFICATION_UNKNOWN_MESSAGE_TYPE")
            };
            // El nombre interno permanece estable: no cambia las claves logicas ya persistidas en fase 2.
            return new(new(id, TripNotificationContracts.MessageType(translated), 1, occurredAt, correlation), translated);
        }
        catch (InvalidNotificationMessageException) { throw; }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or DecoderFallbackException)
        {
            throw Invalid("NOTIFICATION_INVALID_MESSAGE_PAYLOAD");
        }
    }

    public static int RetryCount(NotificationIncomingMessage message)
    {
        var text = ReadHeader(message.Headers, RetryHeader);
        if (text is null) return 0;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var attempt) || attempt < 0)
            throw Invalid("NOTIFICATION_INVALID_RETRY_HEADER");
        return attempt;
    }

    public static string? ReadHeader(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value)) return null;
        return value switch
        {
            byte[] bytes => Utf8.GetString(bytes), string text => text,
            int number => number.ToString(CultureInfo.InvariantCulture), long number => number.ToString(CultureInfo.InvariantCulture),
            short number => number.ToString(CultureInfo.InvariantCulture), byte number => number.ToString(CultureInfo.InvariantCulture),
            _ => throw Invalid("NOTIFICATION_INVALID_HEADER_TYPE")
        };
    }

    private static Guid Id(JsonElement payload, string name)
    {
        if (!payload.GetProperty(name).TryGetGuid(out var id) || id == Guid.Empty) throw Invalid("NOTIFICATION_REQUIRED_ID");
        return id;
    }

    private static Guid[] Ids(JsonElement payload, string name)
    {
        var array = payload.GetProperty(name);
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 1000) throw Invalid("NOTIFICATION_INVALID_STUDENT_LIST");
        return array.EnumerateArray().Select(item =>
            item.TryGetGuid(out var id) && id != Guid.Empty ? id : throw Invalid("NOTIFICATION_INVALID_STUDENT_ID")).ToArray();
    }

    private static InvalidNotificationMessageException Invalid(string code) => new(code);
}
