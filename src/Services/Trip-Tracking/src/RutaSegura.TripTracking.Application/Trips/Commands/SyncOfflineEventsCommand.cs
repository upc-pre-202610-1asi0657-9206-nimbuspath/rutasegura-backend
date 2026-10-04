using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Application.Validation;
using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Trips.Commands;

public enum OfflineEventType
{
    Boarding,
    Alighting,
    Absent,
    Undo,
    Incident,
    Sos
}

/// <summary>Acción que el conductor hizo sin señal y la app guardó en su cola local.</summary>
public sealed record OfflineEventInput(
    Guid ClientEventId,
    OfflineEventType Type,
    DateTimeOffset OccurredAt,
    Guid? StudentId = null,
    PositionInput? Position = null,
    IncidentType? IncidentType = null,
    string? Detail = null,
    bool NotifyGuardians = false);

public enum OfflineEventStatus
{
    Applied,
    Duplicate,
    Rejected
}

public sealed record OfflineEventOutcome(Guid ClientEventId, OfflineEventStatus Status, string? ErrorCode = null);

public sealed record SyncOfflineEventsResult(IReadOnlyList<OfflineEventOutcome> Outcomes)
{
    public int Applied => Outcomes.Count(o => o.Status == OfflineEventStatus.Applied);
    public int Duplicates => Outcomes.Count(o => o.Status == OfflineEventStatus.Duplicate);
    public int Rejected => Outcomes.Count(o => o.Status == OfflineEventStatus.Rejected);
}

/// <summary>
/// Sincronización offline (AC-06, PT-09 Store-and-Forward). Reglas:
/// 1) se aplican en orden de OccurredAt (hora real del evento, no de llegada);
/// 2) cada ClientEventId se aplica una sola vez (reintentos seguros);
/// 3) un evento inválido NO bloquea a los demás: se informa como Rejected con su código,
///    y la app lo saca de la cola para no reintentarlo infinitamente ("Todos los registros enviados").
/// </summary>
public sealed record SyncOfflineEventsCommand(Guid TripId, Guid DriverId, IReadOnlyList<OfflineEventInput> Events)
    : ICommand<SyncOfflineEventsResult>;

internal sealed class SyncOfflineEventsCommandValidator : IValidator<SyncOfflineEventsCommand>
{
    public const int MaxBatchSize = 200;

    public IEnumerable<Error> Validate(SyncOfflineEventsCommand command)
    {
        if (command.Events is null || command.Events.Count == 0)
        {
            yield return Error.Validation("VALIDATION_EVENTS_REQUIRED", "Envía al menos un evento.");
            yield break;
        }

        if (command.Events.Count > MaxBatchSize)
            yield return Error.Validation("VALIDATION_BATCH_TOO_LARGE", $"Máximo {MaxBatchSize} eventos por lote.");

        foreach (var e in command.Events)
        {
            if (e.ClientEventId == Guid.Empty)
                yield return Error.Validation("VALIDATION_CLIENT_EVENT_ID", "Cada evento necesita clientEventId.");

            var needsStudent = e.Type is OfflineEventType.Boarding or OfflineEventType.Alighting
                or OfflineEventType.Absent or OfflineEventType.Undo;
            if (needsStudent && e.StudentId is null)
                yield return Error.Validation("VALIDATION_STUDENT_REQUIRED", $"El evento {e.ClientEventId} necesita studentId.");

            if (e.Type == OfflineEventType.Incident && e.IncidentType is null)
                yield return Error.Validation("VALIDATION_INCIDENT_TYPE", $"El evento {e.ClientEventId} necesita incidentType.");
        }
    }
}

internal sealed class SyncOfflineEventsCommandHandler(ITripRepository trips, TimeProvider clock)
    : ICommandHandler<SyncOfflineEventsCommand, SyncOfflineEventsResult>
{
    public async Task<Result<SyncOfflineEventsResult>> HandleAsync(SyncOfflineEventsCommand command, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadForDriverAsync(command.TripId, command.DriverId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;

        var driverId = new DriverId(command.DriverId);
        var now = clock.GetUtcNow();

        // Se aplican por hora real del evento, pero la respuesta conserva el orden de envío
        // (posición i del lote) para que la app concilie su cola local sin ambigüedad.
        var outcomes = new OfflineEventOutcome[command.Events.Count];
        var ordered = command.Events
            .Select((e, index) => (Event: e, Index: index))
            .OrderBy(x => x.Event.OccurredAt)
            .ThenBy(x => x.Index);

        foreach (var (e, index) in ordered)
        {
            try
            {
                var applied = Apply(trip, driverId, e, now);
                outcomes[index] = new OfflineEventOutcome(e.ClientEventId, applied ? OfflineEventStatus.Applied : OfflineEventStatus.Duplicate);
            }
            catch (DomainException ex)
            {
                outcomes[index] = new OfflineEventOutcome(e.ClientEventId, OfflineEventStatus.Rejected, ex.Code);
            }
        }

        return new SyncOfflineEventsResult(outcomes);
    }

    private static bool Apply(Trip trip, DriverId driverId, OfflineEventInput e, DateTimeOffset now)
    {
        var clientEventId = new ClientEventId(e.ClientEventId);
        var position = e.Position?.ToDomain();

        return e.Type switch
        {
            OfflineEventType.Boarding => trip.RecordBoarding(driverId, new StudentId(e.StudentId!.Value), clientEventId, e.OccurredAt, position, now),
            OfflineEventType.Alighting => trip.RecordAlighting(driverId, new StudentId(e.StudentId!.Value), clientEventId, e.OccurredAt, position, now),
            OfflineEventType.Absent => trip.MarkStudentAbsent(driverId, new StudentId(e.StudentId!.Value), clientEventId, e.OccurredAt, now),
            OfflineEventType.Undo => trip.UndoPassengerRecord(driverId, new StudentId(e.StudentId!.Value), clientEventId, now),
            OfflineEventType.Incident => trip.ReportIncident(driverId, e.IncidentType!.Value, e.Detail, e.NotifyGuardians, clientEventId, e.OccurredAt, position, now),
            OfflineEventType.Sos => trip.RaiseSos(driverId, clientEventId, e.OccurredAt, position, now),
            _ => throw new DomainException("OFFLINE_EVENT_UNKNOWN", $"Tipo de evento desconocido: {e.Type}.")
        };
    }
}
