using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.TripTracking.Api.Errors;
using RutaSegura.TripTracking.Api.Resources;
using RutaSegura.TripTracking.Api.Security;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Application.Trips.Queries;

namespace RutaSegura.TripTracking.Api.Controllers;

/// <summary>
/// Ciclo de vida del viaje (US-10 a US-14): acciones del conductor, aviso del tutor y operaciones de la empresa.
/// Todas las acciones del conductor llevan <c>clientEventId</c>: reenviar la misma petición es seguro
/// (respuesta 200 con <c>applied=false</c>).
/// </summary>
[Route("api/v1/trips")]
[Authorize]
public sealed class TripsController(IDispatcher dispatcher, TimeProvider clock) : TripTrackingControllerBase(dispatcher, clock)
{
    // ───────────────────────── Empresa (ADMIN) ─────────────────────────

    /// <summary>Viajes de mi empresa en un rango (por defecto, hoy). Filtro opcional por estado.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<IReadOnlyList<OrganizationTripDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListForOrganization([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] string? status, CancellationToken ct)
    {
        if (User.OrganizationId() is not { } org) return this.ToProblemCode(ApplicationErrors.OrganizationRequired);
        var (f, t) = RangeOrToday(from, to);
        var query = TripInputs.OrganizationTrips(org, f, t, status);
        return query.IsSuccess ? await Query(query.Value, ct) : this.ToProblemCode(query.FirstError);
    }

    /// <summary>Programa el viaje de una ruta para una fecha (normalmente lo hace el programador automático).</summary>
    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Schedule([FromBody] ScheduleTripResource body, CancellationToken ct)
    {
        if (User.OrganizationId() is not { } org) return this.ToProblemCode(ApplicationErrors.OrganizationRequired);
        return await Send(new ScheduleTripCommand(body.RouteId, body.ServiceDate, org), ct,
            tripId => CreatedAtAction(nameof(GetLiveStatus), new { tripId }, new { tripId }));
    }

    [HttpPost("{tripId:guid}/cancel")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Cancel(Guid tripId, [FromBody] CancelTripResource body, CancellationToken ct)
    {
        if (User.OrganizationId() is not { } org) return this.ToProblemCode(ApplicationErrors.OrganizationRequired);
        return await Send(new CancelTripCommand(tripId, body.Reason, org), ct, _ => NoContent());
    }

    /// <summary>La empresa atiende y cierra la alerta SOS (US-13 S3).</summary>
    [HttpPost("{tripId:guid}/sos/resolve")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> ResolveSos(Guid tripId, CancellationToken ct)
    {
        if (User.OrganizationId() is not { } org) return this.ToProblemCode(ApplicationErrors.OrganizationRequired);
        return await Send(new ResolveSosCommand(tripId, CurrentUserId, org), ct, _ => NoContent());
    }

    // ───────────────────────── Consultas ─────────────────────────

    /// <summary>Estado en vivo. El tutor solo ve a sus hijos y su parada; la ubicación solo con el viaje en curso (AC-01).</summary>
    [HttpGet("{tripId:guid}/live")]
    [Authorize(Policy = Policies.TripViewer)]
    [ProducesResponseType<TripLiveStatusDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> GetLiveStatus(Guid tripId, CancellationToken ct) =>
        Query(new GetTripLiveStatusQuery(tripId, CurrentRequester), ct);

    /// <summary>Solo la posición (desde Redis). Respaldo del canal SignalR para el mapa.</summary>
    [HttpGet("{tripId:guid}/position")]
    [Authorize(Policy = Policies.TripViewer)]
    [ProducesResponseType<TripPositionDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> GetPosition(Guid tripId, CancellationToken ct) =>
        Query(new GetTripPositionQuery(tripId, CurrentRequester), ct);

    /// <summary>Pantalla "Viaje finalizado".</summary>
    [HttpGet("{tripId:guid}/summary")]
    [Authorize(Policy = Policies.Staff)]
    [ProducesResponseType<TripSummaryDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> GetSummary(Guid tripId, CancellationToken ct) =>
        Query(new GetTripSummaryQuery(tripId, CurrentRequester), ct);

    /// <summary>Recorrido GPS aceptado (telemetría persistida).</summary>
    [HttpGet("{tripId:guid}/track")]
    [Authorize(Policy = Policies.Staff)]
    [ProducesResponseType<TripTrackDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> GetTrack(Guid tripId, CancellationToken ct) =>
        Query(new GetTripTrackQuery(tripId, CurrentRequester), ct);

    // ───────────────────────── Conductor (DRIVER) ─────────────────────────

    [HttpPost("{tripId:guid}/start")]
    [Authorize(Policy = Policies.Driver)]
    [ProducesResponseType<DriverActionResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> Start(Guid tripId, [FromBody] DriverActionResource body, CancellationToken ct) =>
        body.ClientEventId == Guid.Empty ? Task.FromResult(MissingClientEventId())
            : Send(new StartTripCommand(tripId, CurrentUserId, body.ClientEventId), ct);

    [HttpPost("{tripId:guid}/complete")]
    [Authorize(Policy = Policies.Driver)]
    [ProducesResponseType<DriverActionResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> Complete(Guid tripId, [FromBody] DriverActionResource body, CancellationToken ct) =>
        body.ClientEventId == Guid.Empty ? Task.FromResult(MissingClientEventId())
            : Send(new CompleteTripCommand(tripId, CurrentUserId, body.ClientEventId), ct);

    /// <summary>Botón "Subió".</summary>
    [HttpPost("{tripId:guid}/boardings")]
    [Authorize(Policy = Policies.Driver)]
    [ProducesResponseType<DriverActionResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> RecordBoarding(Guid tripId, [FromBody] PassengerActionResource body, CancellationToken ct) =>
        body.ClientEventId == Guid.Empty ? Task.FromResult(MissingClientEventId())
            : Send(new RecordBoardingCommand(tripId, CurrentUserId, body.StudentId, body.ClientEventId, body.OccurredAt, ToInput(body.Position)), ct);

    /// <summary>Bajada del alumno (retorno) o entrega.</summary>
    [HttpPost("{tripId:guid}/alightings")]
    [Authorize(Policy = Policies.Driver)]
    [ProducesResponseType<DriverActionResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> RecordAlighting(Guid tripId, [FromBody] PassengerActionResource body, CancellationToken ct) =>
        body.ClientEventId == Guid.Empty ? Task.FromResult(MissingClientEventId())
            : Send(new RecordAlightingCommand(tripId, CurrentUserId, body.StudentId, body.ClientEventId, body.OccurredAt, ToInput(body.Position)), ct);

    /// <summary>Botón "No vino".</summary>
    [HttpPost("{tripId:guid}/absences")]
    [Authorize(Policy = Policies.Driver)]
    [ProducesResponseType<DriverActionResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> MarkAbsent(Guid tripId, [FromBody] AbsenceResource body, CancellationToken ct) =>
        body.ClientEventId == Guid.Empty ? Task.FromResult(MissingClientEventId())
            : Send(new MarkStudentAbsentCommand(tripId, CurrentUserId, body.StudentId, body.ClientEventId, body.OccurredAt), ct);

    /// <summary>Botón "Deshacer" (revierte "Subió" o "No vino").</summary>
    [HttpPost("{tripId:guid}/passengers/{studentId:guid}/undo")]
    [Authorize(Policy = Policies.Driver)]
    [ProducesResponseType<DriverActionResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> Undo(Guid tripId, Guid studentId, [FromBody] DriverActionResource body, CancellationToken ct) =>
        body.ClientEventId == Guid.Empty ? Task.FromResult(MissingClientEventId())
            : Send(new UndoPassengerRecordCommand(tripId, CurrentUserId, studentId, body.ClientEventId), ct);

    /// <summary>Pantalla "Reportar incidente".</summary>
    [HttpPost("{tripId:guid}/incidents")]
    [Authorize(Policy = Policies.Driver)]
    [ProducesResponseType<DriverActionResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReportIncident(Guid tripId, [FromBody] IncidentResource body, CancellationToken ct)
    {
        if (body.ClientEventId == Guid.Empty) return MissingClientEventId();
        var command = TripInputs.ReportIncident(tripId, CurrentUserId, body.ClientEventId, body.IncidentType, body.Detail,
            body.NotifyGuardians, body.OccurredAt, ToInput(body.Position));
        return command.IsSuccess ? await Send(command.Value, ct) : this.ToProblemCode(command.FirstError);
    }

    /// <summary>Botón SOS (después de los 5 s de cancelación en el celular). Nunca falla por falta de GPS.</summary>
    [HttpPost("{tripId:guid}/sos")]
    [Authorize(Policy = Policies.Driver)]
    [ProducesResponseType<DriverActionResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> RaiseSos(Guid tripId, [FromBody] SosResource body, CancellationToken ct) =>
        body.ClientEventId == Guid.Empty ? Task.FromResult(MissingClientEventId())
            : Send(new RaiseSosCommand(tripId, CurrentUserId, body.ClientEventId, body.OccurredAt, ToInput(body.Position)), ct);

    /// <summary>Envía la cola offline (Store-and-Forward). Respuesta por evento: Applied / Duplicate / Rejected.</summary>
    [HttpPost("{tripId:guid}/sync")]
    [Authorize(Policy = Policies.Driver)]
    [ProducesResponseType<SyncOfflineEventsResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Sync(Guid tripId, [FromBody] SyncOfflineEventsResource body, CancellationToken ct)
    {
        var events = new List<OfflineEventInput>(body.Events?.Count ?? 0);
        foreach (var e in body.Events ?? [])
        {
            var input = TripInputs.OfflineEvent(e.ClientEventId, e.Type, e.OccurredAt, e.StudentId, ToInput(e.Position),
                e.IncidentType, e.Detail, e.NotifyGuardians);
            if (input.IsFailure) return this.ToProblemCode(input.FirstError);
            events.Add(input.Value);
        }

        return await Send(new SyncOfflineEventsCommand(tripId, CurrentUserId, events), ct);
    }

    // ───────────────────────── Tutor (PARENT) ─────────────────────────

    /// <summary>"Hoy mi hijo no va": el conductor lo verá en su lista.</summary>
    [HttpPost("{tripId:guid}/guardian-absences")]
    [Authorize(Policy = Policies.Guardian)]
    public Task<IActionResult> NotifyAbsence(Guid tripId, [FromBody] GuardianAbsenceResource body, CancellationToken ct) =>
        Send(new NotifyAbsenceCommand(tripId, CurrentUserId, body.StudentId, body.Note), ct, _ => NoContent());

    private IActionResult MissingClientEventId() =>
        Invalid("VALIDATION_CLIENT_EVENT_ID", "Cada acción del conductor necesita un clientEventId (UUID generado en el celular).");
}
