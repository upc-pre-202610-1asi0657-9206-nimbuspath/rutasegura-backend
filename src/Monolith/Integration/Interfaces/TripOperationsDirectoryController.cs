using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Model.Queries;
using Monolith.Shared.Interfaces.Security;
using Monolith.Stakeholder.Application.QueryServices;

namespace Monolith.Integration.Interfaces;

[ApiController, AllowAnonymous, InternalDirectoryToken(TripTrackingOnly = true)]
[Route("internal/v1/trips/operational-details")]
public sealed class TripOperationsDirectoryController(
    IRoutePlanningQueryService routes, IGuardianAccessQueryService roster) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> GetDetails(TripOperationsRequest request, CancellationToken ct)
    {
        if (request.OrganizationId == Guid.Empty || request.RouteId == Guid.Empty ||
            request.StudentIds is null || request.StudentIds.Length > 500 || request.StudentIds.Contains(Guid.Empty) ||
            request.Stops is null || request.Stops.Length > 30 || request.Stops.Any(stop => stop is null))
            return BadRequest();

        var route = await routes.Handle(new GetRouteByIdQuery(request.OrganizationId, request.RouteId), ct);
        if (route is null) return NotFound();

        var students = request.StudentIds.Length == 0 ? [] :
            await roster.RosterAsync(request.OrganizationId, request.StudentIds, ct);
        var currentStops = route.Stops.OrderBy(stop => stop.Sequence).ToArray();
        var itinerary = route.Itinerary;
        // A previous trip can retain its original stops after the route is edited.
        var sameStops = currentStops.Length == request.Stops.Length &&
            currentStops.Zip(request.Stops).All(pair =>
                pair.First.Id.Identifier == pair.Second.StopId &&
                Math.Abs(pair.First.Location.Latitude - pair.Second.Latitude) < 0.000001 &&
                Math.Abs(pair.First.Location.Longitude - pair.Second.Longitude) < 0.000001);

        return Ok(new
        {
            request.OrganizationId,
            request.RouteId,
            Students = students.Select(student => new { StudentId = student.Id.Identifier, FullName = student.FullName.ToString() }),
            Itinerary = sameStops && itinerary is not null ? new
            {
                Geometry = JsonSerializer.Deserialize<JsonElement>(itinerary.GeometryGeoJson),
                itinerary.DistanceMeters,
                itinerary.DurationSeconds,
                itinerary.CalculatedAt
            } : null
        });
    }
}

public sealed record TripOperationsRequest(Guid OrganizationId, Guid RouteId, Guid[] StudentIds, TripOperationsStop[] Stops);
public sealed record TripOperationsStop(Guid StopId, double Latitude, double Longitude);
