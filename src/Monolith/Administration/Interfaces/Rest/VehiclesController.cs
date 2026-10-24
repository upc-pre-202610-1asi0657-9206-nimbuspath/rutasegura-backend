using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Administration.Application.CommandServices;
using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Model;
using Vehicle = Monolith.Administration.Domain.Model.Aggregates.Vehicle;
using Monolith.Administration.Domain.Model.Commands;
using Monolith.Administration.Domain.Model.Queries;
using Monolith.Administration.Interfaces.Rest.Resources;
using Monolith.Administration.Interfaces.Rest.Transform;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Application.Model;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Swashbuckle.AspNetCore.Annotations;
using Assembler = Monolith.Administration.Interfaces.Rest.Transform.AdministrationResourceFromEntityAssembler;

namespace Monolith.Administration.Interfaces.Rest;

/// <summary>Roster: fleet of the operation and its seating capacity (US-08). Organization administrators only.</summary>
[Route("api/v1/roster/vehicles")]
[Authorize(Policy = IamPolicies.OrganizationAdministrator)]
[SwaggerTag("Administration · Roster: vehicles of the operation.")]
public class VehiclesController(
    IRosterCommandService commandService,
    IRosterQueryService queryService,
    ProblemDetailsFactory problemDetailsFactory) : AdministrationControllerBase(problemDetailsFactory)
{
    [HttpPost]
    [SwaggerOperation(Summary = "Register a vehicle", OperationId = "RegisterVehicle")]
    public async Task<IActionResult> RegisterVehicle(RegisterVehicleResource resource,
        CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new RegisterVehicleCommand(OrganizationId, resource.LicensePlate,
            resource.SeatingCapacity, resource.Brand, resource.Model), cancellationToken);
        return AdministrationActionResultAssembler.ToActionResult(this, result, ProblemDetails,
            vehicle => CreatedAtAction(nameof(GetVehicleById), new { vehicleId = vehicle.Id.Identifier },
                Assembler.ToResourceFromEntity(vehicle)));
    }

    [HttpGet]
    [SwaggerOperation(Summary = "List the vehicles of the organization", OperationId = "GetAllVehicles")]
    public async Task<IActionResult> GetAllVehicles(CancellationToken cancellationToken)
    {
        var vehicles = await queryService.Handle(new GetAllVehiclesQuery(OrganizationId), cancellationToken);
        return Ok(vehicles.Select(Assembler.ToResourceFromEntity));
    }

    [HttpGet("{vehicleId:guid}")]
    [SwaggerOperation(Summary = "Get a vehicle", OperationId = "GetVehicleById")]
    public async Task<IActionResult> GetVehicleById(Guid vehicleId, CancellationToken cancellationToken)
    {
        var vehicle = await queryService.Handle(new GetVehicleByIdQuery(OrganizationId, vehicleId),
            cancellationToken);
        return vehicle is null
            ? AdministrationActionResultAssembler.NotFound(this, ProblemDetails, AdministrationError.VehicleNotFound,
                "Vehicle was not found.")
            : Ok(Assembler.ToResourceFromEntity(vehicle));
    }

    [HttpPut("{vehicleId:guid}/capacity")]
    [SwaggerOperation(Summary = "Change the seating capacity", OperationId = "ChangeVehicleCapacity")]
    public async Task<IActionResult> ChangeVehicleCapacity(Guid vehicleId, ChangeVehicleCapacityResource resource,
        CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(
            new ChangeVehicleCapacityCommand(OrganizationId, vehicleId, resource.SeatingCapacity), cancellationToken));

    [HttpPost("{vehicleId:guid}/enable")]
    [SwaggerOperation(Summary = "Enable a vehicle", OperationId = "EnableVehicle")]
    public async Task<IActionResult> EnableVehicle(Guid vehicleId, CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new EnableVehicleCommand(OrganizationId, vehicleId), cancellationToken));

    [HttpPost("{vehicleId:guid}/disable")]
    [SwaggerOperation(Summary = "Disable a vehicle", OperationId = "DisableVehicle")]
    public async Task<IActionResult> DisableVehicle(Guid vehicleId, CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new DisableVehicleCommand(OrganizationId, vehicleId), cancellationToken));

    private IActionResult Respond(Result<Vehicle> result) =>
        AdministrationActionResultAssembler.ToActionResult(this, result, ProblemDetails,
            vehicle => Ok(Assembler.ToResourceFromEntity(vehicle)));
}
