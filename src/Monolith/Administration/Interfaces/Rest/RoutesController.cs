using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Administration.Application.CommandServices;
using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Commands;
using Monolith.Administration.Domain.Model.Queries;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Interfaces.Rest.Resources;
using Monolith.Administration.Interfaces.Rest.Transform;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Application.Model;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Swashbuckle.AspNetCore.Annotations;
using Assembler = Monolith.Administration.Interfaces.Rest.Transform.AdministrationResourceFromEntityAssembler;
// The Route aggregate is aliased: Microsoft.AspNetCore.Routing.Route and [Route] would otherwise collide.
using RouteAggregate = Monolith.Administration.Domain.Model.Aggregates.Route;

namespace Monolith.Administration.Interfaces.Rest;

/// <summary>
///     Route Planning (US-04, US-05): routes, stops (where each child is picked up / dropped off), students per stop,
///     driver + vehicle assignment and activation within the plan limit. Organization administrators only.
/// </summary>
[Route("api/v1/routes")]
[Authorize(Policy = IamPolicies.OrganizationAdministrator)]
[SwaggerTag("Administration · Route Planning: routes, stops, students and assignments.")]
public class RoutesController(
    IRoutePlanningCommandService commandService,
    IRoutePlanningQueryService queryService,
    IOperationsMonitoringQueryService operationsQueryService,
    ProblemDetailsFactory problemDetailsFactory) : AdministrationControllerBase(problemDetailsFactory)
{
    // ── Routes ───────────────────────────────────────────────────────────────

    [HttpPost]
    [SwaggerOperation(Summary = "Create a route (DRAFT)", OperationId = "CreateRoute")]
    public async Task<IActionResult> CreateRoute(CreateRouteResource resource, CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new CreateRouteCommand(OrganizationId, resource.Name,
            resource.Direction, resource.DepartureTime, resource.EstimatedArrivalTime, TimeZoneOrDefault(resource.TimeZoneId),
            resource.OperatingDays), cancellationToken);
        return AdministrationActionResultAssembler.ToActionResult(this, result, ProblemDetails,
            route => CreatedAtAction(nameof(GetRouteById), new { routeId = route.Id.Identifier },
                Assembler.ToResourceFromEntity(route)));
    }

    [HttpGet]
    [SwaggerOperation(Summary = "List the routes of the organization", OperationId = "GetAllRoutes")]
    public async Task<IActionResult> GetAllRoutes(CancellationToken cancellationToken)
    {
        var routes = await queryService.Handle(new GetAllRoutesQuery(OrganizationId), cancellationToken);
        return Ok(routes.Select(Assembler.ToResourceFromEntity));
    }

    [HttpGet("quota")]
    [SwaggerOperation(Summary = "Active routes against the plan limit", OperationId = "GetActiveRouteQuota")]
    public async Task<IActionResult> GetActiveRouteQuota(CancellationToken cancellationToken)
    {
        var quota = await queryService.Handle(new GetActiveRouteQuotaQuery(OrganizationId), cancellationToken);
        return Ok(Assembler.ToResourceFromModel(quota));
    }

    [HttpGet("{routeId:guid}")]
    [SwaggerOperation(Summary = "Get a route with its stops", OperationId = "GetRouteById")]
    public async Task<IActionResult> GetRouteById(Guid routeId, CancellationToken cancellationToken)
    {
        var route = await queryService.Handle(new GetRouteByIdQuery(OrganizationId, routeId), cancellationToken);
        return route is null
            ? AdministrationActionResultAssembler.NotFound(this, ProblemDetails, AdministrationError.RouteNotFound,
                "Route was not found.")
            : Ok(Assembler.ToResourceFromEntity(route));
    }

    [HttpPut("{routeId:guid}/name")]
    [SwaggerOperation(Summary = "Rename a route", OperationId = "RenameRoute")]
    public async Task<IActionResult> RenameRoute(Guid routeId, RenameRouteResource resource,
        CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new RenameRouteCommand(OrganizationId, routeId, resource.Name),
            cancellationToken));

    [HttpPut("{routeId:guid}/schedule")]
    [SwaggerOperation(Summary = "Change the schedule", OperationId = "RescheduleRoute")]
    public async Task<IActionResult> RescheduleRoute(Guid routeId, RescheduleRouteResource resource,
        CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new RescheduleRouteCommand(OrganizationId, routeId,
            resource.DepartureTime, resource.EstimatedArrivalTime, TimeZoneOrDefault(resource.TimeZoneId),
            resource.OperatingDays), cancellationToken));

    [HttpDelete("{routeId:guid}")]
    [SwaggerOperation(Summary = "Delete a DRAFT or INACTIVE route", OperationId = "DeleteRoute")]
    public async Task<IActionResult> DeleteRoute(Guid routeId, CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new DeleteRouteCommand(OrganizationId, routeId), cancellationToken);
        return AdministrationActionResultAssembler.ToActionResult(this, result, ProblemDetails, _ => NoContent());
    }

    // ── Stops ────────────────────────────────────────────────────────────────

    [HttpPost("{routeId:guid}/stops")]
    [SwaggerOperation(Summary = "Add a stop", OperationId = "AddRouteStop")]
    public async Task<IActionResult> AddStop(Guid routeId, AddRouteStopResource resource,
        CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new AddRouteStopCommand(OrganizationId, routeId, resource.Name,
            resource.Address, resource.Latitude, resource.Longitude, resource.Position), cancellationToken));

    [HttpPut("{routeId:guid}/stops/{stopId:guid}")]
    [SwaggerOperation(Summary = "Update a stop", OperationId = "UpdateRouteStop")]
    public async Task<IActionResult> UpdateStop(Guid routeId, Guid stopId, UpdateRouteStopResource resource,
        CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new UpdateRouteStopCommand(OrganizationId, routeId, stopId,
            resource.Name, resource.Address, resource.Latitude, resource.Longitude), cancellationToken));

    [HttpDelete("{routeId:guid}/stops/{stopId:guid}")]
    [SwaggerOperation(Summary = "Remove an empty stop", OperationId = "RemoveRouteStop")]
    public async Task<IActionResult> RemoveStop(Guid routeId, Guid stopId, CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new RemoveRouteStopCommand(OrganizationId, routeId, stopId),
            cancellationToken));

    [HttpPut("{routeId:guid}/stops/order")]
    [SwaggerOperation(Summary = "Reorder the stops", OperationId = "ReorderRouteStops")]
    public async Task<IActionResult> ReorderStops(Guid routeId, ReorderRouteStopsResource resource,
        CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new ReorderRouteStopsCommand(OrganizationId, routeId, resource.StopIds),
            cancellationToken));

    // ── Students per stop ────────────────────────────────────────────────────

    [HttpPut("{routeId:guid}/stops/{stopId:guid}/students/{studentId:guid}")]
    [SwaggerOperation(Summary = "Set the stop of a student (moves it if it was on another stop)",
        OperationId = "AssignStudentToStop")]
    public async Task<IActionResult> AssignStudentToStop(Guid routeId, Guid stopId, Guid studentId,
        CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(
            new AssignStudentToStopCommand(OrganizationId, routeId, stopId, studentId), cancellationToken));

    [HttpDelete("{routeId:guid}/students/{studentId:guid}")]
    [SwaggerOperation(Summary = "Remove a student from the route", OperationId = "UnassignStudentFromRoute")]
    public async Task<IActionResult> UnassignStudent(Guid routeId, Guid studentId,
        CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new UnassignStudentFromRouteCommand(OrganizationId, routeId, studentId),
            cancellationToken));

    // ── Assignment and lifecycle ─────────────────────────────────────────────

    [HttpPut("{routeId:guid}/assignment")]
    [SwaggerOperation(Summary = "Assign driver and vehicle (US-05)", OperationId = "AssignRouteResources")]
    public async Task<IActionResult> AssignResources(Guid routeId, AssignRouteResourcesResource resource,
        CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new AssignRouteResourcesCommand(OrganizationId, routeId,
            resource.DriverId, resource.VehicleId), cancellationToken));

    [HttpDelete("{routeId:guid}/assignment")]
    [SwaggerOperation(Summary = "Release driver and vehicle of a non-active route",
        OperationId = "ReleaseRouteResources")]
    public async Task<IActionResult> ReleaseResources(Guid routeId, CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new ReleaseRouteResourcesCommand(OrganizationId, routeId),
            cancellationToken));

    [HttpPost("{routeId:guid}/activate")]
    [SwaggerOperation(Summary = "Activate (publishes the route to Trip & Tracking; plan limit applies)",
        OperationId = "ActivateRoute")]
    public async Task<IActionResult> ActivateRoute(Guid routeId, CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new ActivateRouteCommand(OrganizationId, routeId), cancellationToken));

    [HttpPost("{routeId:guid}/deactivate")]
    [SwaggerOperation(Summary = "Deactivate (retires the route in Trip & Tracking)", OperationId = "DeactivateRoute")]
    public async Task<IActionResult> DeactivateRoute(Guid routeId, CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new DeactivateRouteCommand(OrganizationId, routeId), cancellationToken));

    // ── History ──────────────────────────────────────────────────────────────

    [HttpGet("{routeId:guid}/trip-records")]
    [SwaggerOperation(Summary = "Completed trips of the route", OperationId = "GetTripRecordsByRoute")]
    public async Task<IActionResult> GetTripRecords(Guid routeId, CancellationToken cancellationToken)
    {
        var records = await operationsQueryService.Handle(new GetTripRecordsByRouteQuery(OrganizationId, routeId),
            cancellationToken);
        return Ok(records.Select(Assembler.ToResourceFromEntity));
    }

    private IActionResult Respond(Result<RouteAggregate> result) =>
        AdministrationActionResultAssembler.ToActionResult(this, result, ProblemDetails,
            route => Ok(Assembler.ToResourceFromEntity(route)));

    private static string TimeZoneOrDefault(string? timeZoneId) =>
        string.IsNullOrWhiteSpace(timeZoneId) ? RouteSchedule.DefaultTimeZoneId : timeZoneId;
}
