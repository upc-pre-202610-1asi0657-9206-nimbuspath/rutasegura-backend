using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Application.Trips.Queries;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Common;

/// <summary>
/// Fábricas que reciben texto plano desde los adaptadores de entrada (REST, SignalR, mensajería).
/// Así la API no necesita referenciar enums del dominio (regla de dependencias de la arquitectura:
/// Api → Application, nunca Api → Domain).
/// </summary>
public static class TripInputs
{
    public static IReadOnlyList<string> IncidentTypes { get; } = Enum.GetNames<IncidentType>();

    public static IReadOnlyList<string> TripStatuses { get; } = Enum.GetNames<TripStatus>();

    public static Result<ReportIncidentCommand> ReportIncident(
        Guid tripId, Guid driverId, Guid clientEventId, string? type, string? detail, bool notifyGuardians,
        DateTimeOffset? occurredAt = null, PositionInput? position = null)
    {
        if (!TryParseIncidentType(type, out var incidentType)) return InvalidIncidentType(type);
        return new ReportIncidentCommand(tripId, driverId, clientEventId, incidentType, detail, notifyGuardians, occurredAt, position);
    }

    public static Result<OfflineEventInput> OfflineEvent(
        Guid clientEventId, string? type, DateTimeOffset occurredAt, Guid? studentId = null, PositionInput? position = null,
        string? incidentType = null, string? detail = null, bool notifyGuardians = false)
    {
        if (!Enum.TryParse<OfflineEventType>(type, ignoreCase: true, out var eventType) || !Enum.IsDefined(eventType) || int.TryParse(type, out _))
            return Error.Validation("VALIDATION_EVENT_TYPE",
                $"Tipo de evento '{type}' desconocido. Valores: {string.Join(", ", Enum.GetNames<OfflineEventType>())}.");

        IncidentType? parsedIncident = null;
        if (incidentType is not null)
        {
            if (!TryParseIncidentType(incidentType, out var value)) return InvalidIncidentType(incidentType);
            parsedIncident = value;
        }

        return new OfflineEventInput(clientEventId, eventType, occurredAt, studentId, position, parsedIncident, detail, notifyGuardians);
    }

    public static Result<GetOrganizationTripsQuery> OrganizationTrips(Guid organizationId, DateTimeOffset from, DateTimeOffset to, string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return new GetOrganizationTripsQuery(organizationId, from, to);
        if (!Enum.TryParse<TripStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(status, out _))
            return Error.Validation("VALIDATION_TRIP_STATUS", $"Estado '{status}' desconocido. Valores: {string.Join(", ", TripStatuses)}.");
        return new GetOrganizationTripsQuery(organizationId, from, to, parsed);
    }

    private static bool TryParseIncidentType(string? value, out IncidentType type) =>
        Enum.TryParse(value?.Replace("_", string.Empty), ignoreCase: true, out type) && Enum.IsDefined(type) && !int.TryParse(value, out _);

    private static Error InvalidIncidentType(string? value) =>
        Error.Validation("VALIDATION_INCIDENT_TYPE", $"Tipo de incidente '{value}' desconocido. Valores: {string.Join(", ", IncidentTypes)}.");
}
