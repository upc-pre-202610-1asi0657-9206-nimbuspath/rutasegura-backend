using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.TripTracking.Application.Trips.Queries;

namespace RutaSegura.TripTracking.Api.Controllers;

/// <summary>Puerto interno minimo para caducar proximidades; no expone GPS ni alumnos.</summary>
[ApiController]
[AllowAnonymous] // Autenticacion de servicio independiente del JWT de usuarios, validada antes de acceder a la BD.
[Route("internal/v1/trips/{tripId:guid}/notification-context")]
public sealed class NotificationContextController(IConfiguration configuration, IDispatcher dispatcher) : ControllerBase
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
        var result = await dispatcher.QueryAsync(new GetTripNotificationContextQuery(tripId, organizationId), ct);
        if (result.IsFailure) return NotFound();
        return Ok(new { organizationId, tripId, isActive = result.Value.IsActive });
    }
}
