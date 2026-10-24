using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Domain.Common;

namespace RutaSegura.TripTracking.Api.Controllers;

/// <summary>Puerto interno minimo para caducar proximidades; no expone GPS ni alumnos.</summary>
[ApiController]
[AllowAnonymous] // Autenticacion de servicio independiente del JWT de usuarios, validada antes de acceder a la BD.
[Route("internal/v1/trips/{tripId:guid}/notification-context")]
public sealed class NotificationContextController(IConfiguration configuration, ITripRepository trips) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid tripId, [FromQuery] Guid organizationId, CancellationToken ct)
    {
        var expected = configuration["InternalServices:NotificationToken"];
        var supplied = Request.Headers["X-Service-Token"].ToString();
        if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32 ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            return Unauthorized();
        if (organizationId == Guid.Empty) return BadRequest();
        var trip = await trips.GetByIdAsync(new TripId(tripId), ct);
        if (trip is null || trip.OrganizationId.Value != organizationId) return NotFound();
        return Ok(new { organizationId, tripId, isActive = trip.IsActive });
    }
}
