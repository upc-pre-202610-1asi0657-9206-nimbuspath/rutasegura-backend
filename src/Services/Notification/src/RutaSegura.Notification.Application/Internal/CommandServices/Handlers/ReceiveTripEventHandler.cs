using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Internal.Policies;
using RutaSegura.Notification.Application.Internal.Validators;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Application.Contracts.V1;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Application.Internal.Transform;

namespace RutaSegura.Notification.Application.Internal.CommandServices.Handlers;

internal sealed class ReceiveTripEventHandler(INotificationRepository notifications,
    INotificationRecipientDirectory recipients, ITripNotificationContext tripContext,
    INotificationDispatchQueue dispatchQueue, IProcessedNotificationEventRepository processedEvents,
    TimeProvider clock, NotificationProcessingPolicy policy)
    : ICommandHandler<ReceiveTripEventCommand, ReceiveTripEventResult>
{
    public async Task<Result<ReceiveTripEventResult>> HandleAsync(ReceiveTripEventCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // La misma validacion protege la entrada del consumidor; no se procesa un contrato desconocido.
        var validation = new ReceiveTripEventValidator().Validate(command).ToArray();
        if (validation.Length > 0) return Result<ReceiveTripEventResult>.Failure(validation);
        var e = command.Event;
        var now = clock.GetUtcNow();
        var fingerprint = NotificationEventFingerprint.Create(command);
        var processed = await processedEvents.FindAsync(TripNotificationContracts.ConsumerQueue,
            command.Metadata.MessageId, cancellationToken);
        if (processed is not null)
        {
            if (processed.OrganizationId != e.OrganizationId || processed.PayloadHash != fingerprint)
                return Error.Conflict("NOTIFICATION_SOURCE_CONFLICT", "La identidad del evento ya corresponde a otro contenido.");
            return new ReceiveTripEventResult(0, processed.NotificationCount, processed.SkippedReason);
        }

        ReceiveTripEventResult Complete(ReceiveTripEventResult result)
        {
            processedEvents.Add(ProcessedNotificationEvent.Record(TripNotificationContracts.ConsumerQueue,
                command.Metadata.MessageId, e.OrganizationId, command.Metadata.MessageType, fingerprint,
                command.Metadata.OccurredAt, now, result.Created + result.Duplicates, result.SkippedReason));
            return result;
        }
        if (command.Metadata.OccurredAt > now + policy.MaxFutureClockSkew)
            return Error.Validation("NOTIFICATION_EVENT_IN_FUTURE", "El instante del hecho no puede estar en el futuro.");
        if (e is IncidentReportedV1 { NotifyGuardians: false })
            return Complete(new ReceiveTripEventResult(0, 0, "GUARDIAN_NOTICE_NOT_REQUESTED"));
        if (e is TripCompletedV1 { Direction: TripDirectionV1.Return })
            return Complete(new ReceiveTripEventResult(0, 0, "RETURN_DELIVERY_REPORTED_BY_ALIGHTING"));
        if (e is VehicleApproachingStopV1)
        {
            if (now >= command.Metadata.OccurredAt + policy.ProximityLifetime)
                return Complete(new ReceiveTripEventResult(0, 0, "PROXIMITY_EXPIRED"));
            if (!await tripContext.IsActiveAsync(e.OrganizationId, e.TripId, cancellationToken))
                return Complete(new ReceiveTripEventResult(0, 0, "TRIP_NOT_ACTIVE"));
        }

        Guid[] students = e switch
        {
            StudentBoardedV1 b => [b.StudentId], StudentAlightedV1 a => [a.StudentId],
            VehicleApproachingStopV1 p => p.StudentIds.Distinct().ToArray(),
            IncidentReportedV1 i => i.AffectedStudentIds.Distinct().ToArray(),
            SosRaisedV1 s => s.AffectedStudentIds.Distinct().ToArray(),
            TripCompletedV1 c => c.SchoolDeliveredStudentIds.Distinct().ToArray(), _ => []
        };
        var audience = students.Length == 0 ? [] :
            (await recipients.ForStudentsAsync(e.OrganizationId, students, cancellationToken)).ToList();
        if (audience.Any(r => r.OrganizationId != e.OrganizationId || r.UserId == Guid.Empty ||
            r.StudentId is null || !students.Contains(r.StudentId.Value)))
            return InvalidAudience;
        if (e is SosRaisedV1)
        {
            var operators = await recipients.OperatorsAsync(e.OrganizationId, cancellationToken);
            if (operators.Any(r => r.OrganizationId != e.OrganizationId || r.UserId == Guid.Empty || r.StudentId is not null))
                return InvalidAudience;
            audience.AddRange(operators);
        }

        var (category, title, body) = Describe(e);
        DateTimeOffset? expiresAt = e is VehicleApproachingStopV1 ? command.Metadata.OccurredAt + policy.ProximityLifetime : null;
        var additions = new List<NotificationEntity>();
        var duplicates = 0;
        foreach (var recipient in audience.Distinct())
        {
            var notification = NotificationEntity.Create(e.OrganizationId, recipient.UserId,
                command.Metadata.MessageId, command.Metadata.MessageType, command.Metadata.CorrelationId, category,
                NotificationContent.Create(title, body), command.Metadata.OccurredAt, now,
                tripId: e.TripId, studentId: recipient.StudentId, expiresAt: expiresAt);
            var existing = await notifications.FindByKeyAsync(notification.Key, cancellationToken);
            if (existing is not null)
            {
                if (existing.TripId != notification.TripId || existing.Content != notification.Content ||
                    existing.OccurredAt != notification.OccurredAt)
                    return Error.Conflict("NOTIFICATION_SOURCE_CONFLICT", "La identidad del evento ya corresponde a otro contenido.");
                duplicates++;
            }
            else additions.Add(notification);
        }
        // No hay cambios parciales antes de validar todo el lote. El decorador confirma una sola vez.
        foreach (var notification in additions)
        {
            notifications.Add(notification);
            dispatchQueue.Enqueue(notification);
        }
        return Complete(new ReceiveTripEventResult(additions.Count, duplicates));
    }

    private static readonly Error InvalidAudience = Error.Forbidden("NOTIFICATION_INVALID_AUDIENCE",
        "El directorio devolvio destinatarios fuera del alcance autorizado.");

    private static (NotificationCategory, string, string) Describe(TripNotificationEventV1 e) => e switch
    {
        StudentBoardedV1 => (NotificationCategory.Boarding, "Abordaje confirmado", "Se confirmo el abordaje de tu alumno."),
        StudentAlightedV1 => (NotificationCategory.Alighting, "Desembarque confirmado", "Se confirmo el desembarque de tu alumno."),
        VehicleApproachingStopV1 p => (NotificationCategory.Proximity, "El vehiculo se acerca", $"Llegada estimada a la parada: {p.EtaMinutes} min."),
        IncidentReportedV1 => (NotificationCategory.Incident, "Incidencia en el viaje", "Se registro una incidencia. Consulta el detalle autorizado del viaje."),
        SosRaisedV1 => (NotificationCategory.Sos, "Alerta SOS", "Se registro una emergencia. Consulta el viaje por el canal autorizado."),
        TripCompletedV1 => (NotificationCategory.Arrival, "Entrega confirmada", "Se confirmo la entrega de tu alumno al finalizar el traslado."),
        _ => throw new ArgumentException("Contrato no soportado.", nameof(e))
    };
}
