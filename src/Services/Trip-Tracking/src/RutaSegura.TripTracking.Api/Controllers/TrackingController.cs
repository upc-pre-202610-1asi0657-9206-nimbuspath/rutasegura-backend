using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.TripTracking.Api.Resources;
using RutaSegura.TripTracking.Api.Security;
using RutaSegura.TripTracking.Application.Trips.Commands;

namespace RutaSegura.TripTracking.Api.Controllers;

/// <summary>Ingesta de telemetría GPS del celular del conductor (TS-15, QA-2/QA-5).</summary>
[Route("api/v1/tracking")]
[Authorize(Policy = Policies.Driver)]
public sealed class TrackingController(IDispatcher dispatcher, TimeProvider clock) : TripTrackingControllerBase(dispatcher, clock)
{
    public const string TelemetryRateLimit = "telemetry";

    /// <summary>
    /// Registra un lote de lecturas (≤ 500). Las lecturas ruidosas se ignoran sin fallar el lote; la respuesta
    /// indica cuántas se aceptaron. Si el viaje no está en curso responde 422 TRACKING_REQUIRES_ACTIVE_TRIP:
    /// la app debe dejar de compartir la ubicación.
    /// </summary>
    [HttpPost("positions")]
    [EnableRateLimiting(TelemetryRateLimit)]
    [ProducesResponseType<RecordPositionsResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> RecordPositions([FromBody] RecordPositionsResource body, CancellationToken ct) =>
        Send(new RecordPositionsCommand(body.TripId, CurrentUserId,
            (body.Readings ?? []).Select(r => new GpsReadingInput(r.Latitude, r.Longitude, r.RecordedAt, r.AccuracyMeters, r.SpeedKmh, r.HeadingDegrees)).ToList()),
            ct);
}
