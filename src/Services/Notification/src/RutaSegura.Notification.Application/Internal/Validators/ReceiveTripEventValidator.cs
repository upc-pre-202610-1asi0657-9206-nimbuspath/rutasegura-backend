using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Application.Validation;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Contracts.V1;
using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Application.Internal.Validators;

internal sealed class ReceiveTripEventValidator : IValidator<ReceiveTripEventCommand>
{
    public IEnumerable<Error> Validate(ReceiveTripEventCommand command)
    {
        var m = command.Metadata;
        var e = command.Event;
        if (m is null || e is null)
        {
            yield return Error.Validation("NOTIFICATION_EVENT_REQUIRED", "Se requiere el evento y sus metadatos.");
            yield break;
        }
        if (m.MessageId == Guid.Empty || m.OccurredAt == default || string.IsNullOrWhiteSpace(m.CorrelationId) || m.CorrelationId.Length > 200)
            yield return Error.Validation("NOTIFICATION_INVALID_EVENT_METADATA", "El evento requiere identidad, fecha y correlacion.");
        var supported = e is StudentBoardedV1 or StudentAlightedV1 or VehicleApproachingStopV1 or
            IncidentReportedV1 or SosRaisedV1 or TripCompletedV1;
        if (!supported || m.SchemaVersion != 1 || (supported && m.MessageType != TripNotificationContracts.MessageType(e)))
            yield return Error.Validation("NOTIFICATION_UNSUPPORTED_CONTRACT", "El tipo y la version deben coincidir con el contrato.");
        if (e.OrganizationId == Guid.Empty || e.TripId == Guid.Empty)
            yield return Error.Validation("NOTIFICATION_INVALID_EVENT_SCOPE", "Se requiere organizacion y viaje.");
        var valid = e switch
        {
            StudentBoardedV1 b => b.StudentId != Guid.Empty && b.StopId != Guid.Empty,
            StudentAlightedV1 a => a.StudentId != Guid.Empty && a.StopId != Guid.Empty,
            VehicleApproachingStopV1 p => p.StopId != Guid.Empty && ValidIds(p.StudentIds, true) && p.EtaMinutes >= 0,
            IncidentReportedV1 i => i.IncidentId != Guid.Empty && ValidIds(i.AffectedStudentIds, false),
            SosRaisedV1 s => s.DriverId != Guid.Empty && s.VehicleId != Guid.Empty && ValidIds(s.AffectedStudentIds, false),
            TripCompletedV1 c => c.RouteId != Guid.Empty && c.Direction is TripDirectionV1.Pickup or TripDirectionV1.Return &&
                ValidIds(c.SchoolDeliveredStudentIds, false),
            _ => false
        };
        if (!valid) yield return Error.Validation("NOTIFICATION_INVALID_EVENT_PAYLOAD", "El contenido del evento no es valido.");
    }

    private static bool ValidIds(IReadOnlyList<Guid>? ids, bool requireAny) =>
        ids is not null && (!requireAny || ids.Count > 0) && ids.All(id => id != Guid.Empty);
}
