using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Queries;
using Monolith.Administration.Interfaces.Rest.Resources;
using Monolith.Administration.Interfaces.Rest.Transform;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Swashbuckle.AspNetCore.Annotations;
using Assembler = Monolith.Administration.Interfaces.Rest.Transform.AdministrationResourceFromEntityAssembler;

namespace Monolith.Administration.Interfaces.Rest;

/// <summary>The authenticated driver (mobile app) reads its own profile and the routes it operates.</summary>
[Route("api/v1/roster/drivers/me")]
[Authorize(Policy = IamPolicies.OrganizationDriver)]
[SwaggerTag("Administration · Roster: profile of the authenticated driver.")]
public class DriverProfileController(
    IRosterQueryService queryService,
    IRoutePlanningQueryService routeQueryService,
    ProblemDetailsFactory problemDetailsFactory) : AdministrationControllerBase(problemDetailsFactory)
{
    [HttpGet]
    [SwaggerOperation(Summary = "My driver profile", OperationId = "GetMyDriverProfile")]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        var driver = await queryService.Handle(new GetDriverByUserIdQuery(OrganizationId, CurrentUserId),
            cancellationToken);
        return driver is null
            ? AdministrationActionResultAssembler.NotFound(this, ProblemDetails, AdministrationError.DriverNotFound,
                "The account has no driver profile in the roster.")
            : Ok(Assembler.ToResourceFromEntity(driver));
    }

    [HttpGet("routes")]
    [SwaggerOperation(Summary = "Routes I operate", OperationId = "GetMyRoutes")]
    public async Task<IActionResult> GetMyRoutes(CancellationToken cancellationToken)
    {
        var driver = await queryService.Handle(new GetDriverByUserIdQuery(OrganizationId, CurrentUserId),
            cancellationToken);
        if (driver is null)
            return AdministrationActionResultAssembler.NotFound(this, ProblemDetails,
                AdministrationError.DriverNotFound, "The account has no driver profile in the roster.");

        var routes = await routeQueryService.Handle(
            new GetRoutesByDriverQuery(OrganizationId, driver.Id.Identifier), cancellationToken);
        // The enrollment code is for the administrator and the families, not for the driver.
        return Ok(routes.Select(route => Assembler.ToResourceFromEntity(route) with { EnrollmentCode = null }));
    }

    /// <summary>Plate, model and seats of the vehicles assigned to the routes the driver operates.</summary>
    [HttpGet("vehicles")]
    [SwaggerOperation(Summary = "Vehicles of the routes I operate", OperationId = "GetMyVehicles")]
    public async Task<IActionResult> GetMyVehicles(CancellationToken cancellationToken)
    {
        var driver = await queryService.Handle(new GetDriverByUserIdQuery(OrganizationId, CurrentUserId),
            cancellationToken);
        if (driver is null)
            return AdministrationActionResultAssembler.NotFound(this, ProblemDetails,
                AdministrationError.DriverNotFound, "The account has no driver profile in the roster.");

        var routes = (await routeQueryService.Handle(
            new GetRoutesByDriverQuery(OrganizationId, driver.Id.Identifier), cancellationToken)).ToList();
        var vehicles = new List<DriverVehicleResource>();
        foreach (var group in routes.Where(route => route.Assignment is not null)
                     .GroupBy(route => route.Assignment!.VehicleId))
        {
            var vehicle = await queryService.Handle(new GetVehicleByIdQuery(OrganizationId, group.Key.Identifier),
                cancellationToken);
            if (vehicle is null) continue;
            vehicles.Add(new DriverVehicleResource(vehicle.Id.Identifier, vehicle.LicensePlate.Value,
                vehicle.Capacity.Value, vehicle.Model.Brand, vehicle.Model.Model, vehicle.Status.Value,
                group.Select(route => route.Id.Identifier).ToList()));
        }

        return Ok(vehicles);
    }
}
