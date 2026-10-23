using System.Text.Json;
using RutaSegura.Notification.Infrastructure.Messaging.Inbound;

namespace RutaSegura.Notification.Messaging.Tests;

internal static class MessageSamples
{
    public static readonly Guid Organization = Guid.NewGuid(), Trip = Guid.NewGuid(), Student = Guid.NewGuid(),
        Guardian = Guid.NewGuid(), Operator = Guid.NewGuid(), Stop = Guid.NewGuid(), Route = Guid.NewGuid();
    public static NotificationIncomingMessage Message(string type = "trip-tracking.student-boarded", Guid? id = null, object? payload = null)
    {
        var now = DateTimeOffset.UtcNow;
        payload ??= new { organizationId = Organization, tripId = Trip, studentId = Student, stopId = Stop,
            routeId = Route, studentsAtStop = new[] { Student }, etaMinutes = 2, incidentId = Guid.NewGuid(), notifyGuardians = true,
            affectedStudents = new[] { Student }, driverId = Guid.NewGuid(), vehicleId = Guid.NewGuid(), studentsOnBoard = new[] { Student },
            direction = "Pickup", schoolDeliveredStudentIds = new[] { Student } };
        return new((id ?? Guid.NewGuid()).ToString(), type, "messaging-test", type,
            new Dictionary<string, object?> { ["message-type"] = type, ["schema-version"] = 1,
                ["occurred-at"] = now.ToString("O"), ["correlation-id"] = "messaging-test" },
            JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)), UnixTimestamp: now.ToUnixTimeSeconds());
    }
}
