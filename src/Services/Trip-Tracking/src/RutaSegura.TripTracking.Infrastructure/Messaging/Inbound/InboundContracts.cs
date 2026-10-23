using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Replicas;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Infrastructure.Messaging.Inbound;

// ─────────────────────────────────────────────────────────────────────────────
// Contratos que Trip & Tracking CONSUME (Published Language de otros contextos).
// Se identifican por el sufijo del tipo de mensaje, así no importa si lo publica
// "administration.*" o "iam.*" (Roster aún puede moverse entre servicios).
// Este archivo es el acuerdo con los equipos de Administration e IAM.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record RouteStopV1(Guid StopId, int Sequence, string Name, double Latitude, double Longitude);

public sealed record RouteStudentV1(Guid StudentId, Guid StopId);

/// <summary>
/// <c>*.route-published</c> / <c>*.route-updated</c>: snapshot completo de la ruta planificada.
/// <c>departureTime</c> en formato HH:mm (hora local de <c>timeZoneId</c>, ej. "America/Lima").
/// <c>direction</c>: "Pickup" | "Return". <c>operatingDays</c>: "Monday".."Sunday".
/// </summary>
public sealed record RoutePublishedV1(
    Guid RouteId, Guid? OrganizationId, string Name, string Direction, Guid DriverId, Guid VehicleId,
    string DepartureTime, string TimeZoneId, IReadOnlyList<string> OperatingDays,
    IReadOnlyList<RouteStopV1> Stops, IReadOnlyList<RouteStudentV1> Students);

/// <summary><c>*.route-retired</c>: la ruta deja de operar; no se programan más viajes.</summary>
public sealed record RouteRetiredV1(Guid RouteId);

/// <summary><c>*.guardian-student-linked</c> / <c>*.guardian-student-unlinked</c> (US-02).</summary>
public sealed record GuardianStudentLinkV1(Guid GuardianId, Guid StudentId);

/// <summary>Traduce un mensaje entrante al comando de Application correspondiente (Anti-Corruption Layer).</summary>
internal static class InboundMessageTranslator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public const string RoutePublished = "route-published";
    public const string RouteUpdated = "route-updated";
    public const string RouteRetired = "route-retired";
    public const string GuardianLinked = "guardian-student-linked";
    public const string GuardianUnlinked = "guardian-student-unlinked";

    /// <summary>
    /// Devuelve el comando a ejecutar, <c>null</c> si el tipo no interesa a este servicio (se confirma y descarta),
    /// o lanza <see cref="InvalidInboundMessageException"/> si el contenido es inválido (va a la DLQ).
    /// </summary>
    public static ICommand<Unit>? Translate(string messageType, ReadOnlySpan<byte> body)
    {
        var suffix = messageType.Contains('.') ? messageType[(messageType.LastIndexOf('.') + 1)..] : messageType;
        try
        {
            return suffix switch
            {
                RoutePublished or RouteUpdated => new UpsertRouteSnapshotCommand(ToSnapshot(Read<RoutePublishedV1>(body))),
                RouteRetired => new RemoveRouteSnapshotCommand(Read<RouteRetiredV1>(body).RouteId),
                GuardianLinked => Read<GuardianStudentLinkV1>(body) is var l ? new LinkGuardianStudentCommand(l.GuardianId, l.StudentId) : null,
                GuardianUnlinked => Read<GuardianStudentLinkV1>(body) is var u ? new UnlinkGuardianStudentCommand(u.GuardianId, u.StudentId) : null,
                _ => null
            };
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or NotSupportedException)
        {
            throw new InvalidInboundMessageException($"Contenido inválido para {messageType}: {ex.Message}", ex);
        }
    }

    private static T Read<T>(ReadOnlySpan<byte> body) =>
        JsonSerializer.Deserialize<T>(body, Json) ?? throw new JsonException("Cuerpo vacío.");

    internal static RouteSnapshot ToSnapshot(RoutePublishedV1 m) => new(
        m.RouteId,
        m.Name,
        Enum.Parse<TripDirection>(m.Direction, ignoreCase: true),
        m.DriverId,
        m.VehicleId,
        TimeOnly.Parse(m.DepartureTime, CultureInfo.InvariantCulture),
        m.TimeZoneId,
        (m.OperatingDays ?? []).Select(d => Enum.Parse<DayOfWeek>(d, ignoreCase: true)).Distinct().ToList(),
        (m.Stops ?? []).Select(s => new RouteStopSnapshot(s.StopId, s.Sequence, s.Name, s.Latitude, s.Longitude)).ToList(),
        (m.Students ?? []).Select(s => new RouteStudentSnapshot(s.StudentId, s.StopId)).ToList(),
        m.OrganizationId ?? Guid.Empty);
}

public sealed class InvalidInboundMessageException(string message, Exception? inner = null) : Exception(message, inner);
