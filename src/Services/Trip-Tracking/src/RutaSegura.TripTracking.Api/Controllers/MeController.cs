using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.TripTracking.Api.Security;
using RutaSegura.TripTracking.Application.Trips.Queries;

namespace RutaSegura.TripTracking.Api.Controllers;

/// <summary>Pantallas de inicio: agenda del conductor y viajes de los hijos del tutor.</summary>
[Route("api/v1")]
[Authorize]
public sealed class MeController(IDispatcher dispatcher, TimeProvider clock) : TripTrackingControllerBase(dispatcher, clock)
{
    /// <summary>"Tu próximo viaje" y "Más tarde hoy". Por defecto, los viajes de hoy (hora de Lima).</summary>
    [HttpGet("drivers/me/trips")]
    [Authorize(Policy = Policies.Driver)]
    [ProducesResponseType<IReadOnlyList<DriverTripDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> DriverAgenda([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        var (f, t) = RangeOrToday(from, to);
        return Query(new GetDriverAgendaQuery(CurrentUserId, f, t), ct);
    }

    /// <summary>Viajes de hoy en los que van mis hijos, con su estado (solo mis hijos, AC-01).</summary>
    [HttpGet("guardians/me/trips")]
    [Authorize(Policy = Policies.Guardian)]
    [ProducesResponseType<IReadOnlyList<GuardianTripDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> GuardianTrips([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct)
    {
        var (f, t) = RangeOrToday(from, to);
        return Query(new GetGuardianTripsQuery(CurrentUserId, f, t), ct);
    }
}
