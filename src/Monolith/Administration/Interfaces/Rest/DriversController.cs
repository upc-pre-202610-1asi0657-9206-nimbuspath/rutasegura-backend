using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Administration.Application.CommandServices;
using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Model;
using Driver = Monolith.Administration.Domain.Model.Aggregates.Driver;
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

/// <summary>Roster: drivers of the operation (US-01). Organization administrators only.</summary>
[Route("api/v1/roster/drivers")]
[Authorize(Policy = IamPolicies.OrganizationAdministrator)]
[SwaggerTag("Administration · Roster: drivers of the operation.")]
public class DriversController(
    IRosterCommandService commandService,
    IRosterQueryService queryService,
    IRoutePlanningQueryService routeQueryService,
    ProblemDetailsFactory problemDetailsFactory) : AdministrationControllerBase(problemDetailsFactory)
{
    [HttpPost]
    [SwaggerOperation(Summary = "Register a driver (pending verification)", OperationId = "RegisterDriver")]
    public async Task<IActionResult> RegisterDriver(RegisterDriverResource resource,
        CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new RegisterDriverCommand(OrganizationId, resource.UserId,
            resource.FirstName, resource.LastName, resource.DocumentNumber, resource.LicenseNumber,
            resource.LicenseCategory, resource.LicenseExpiresOn, resource.PhoneNumber), cancellationToken);
        return AdministrationActionResultAssembler.ToActionResult(this, result, ProblemDetails,
            driver => CreatedAtAction(nameof(GetDriverById), new { driverId = driver.Id.Identifier },
                Assembler.ToResourceFromEntity(driver)));
    }

    [HttpGet]
    [SwaggerOperation(Summary = "List the drivers of the organization", OperationId = "GetAllDrivers")]
    public async Task<IActionResult> GetAllDrivers(CancellationToken cancellationToken)
    {
        var drivers = await queryService.Handle(new GetAllDriversQuery(OrganizationId), cancellationToken);
        return Ok(drivers.Select(Assembler.ToResourceFromEntity));
    }

    [HttpGet("{driverId:guid}")]
    [SwaggerOperation(Summary = "Get a driver", OperationId = "GetDriverById")]
    public async Task<IActionResult> GetDriverById(Guid driverId, CancellationToken cancellationToken)
    {
        var driver = await queryService.Handle(new GetDriverByIdQuery(OrganizationId, driverId), cancellationToken);
        return driver is null
            ? AdministrationActionResultAssembler.NotFound(this, ProblemDetails, AdministrationError.DriverNotFound,
                "Driver was not found.")
            : Ok(Assembler.ToResourceFromEntity(driver));
    }

    [HttpGet("{driverId:guid}/routes")]
    [SwaggerOperation(Summary = "Routes assigned to a driver", OperationId = "GetRoutesByDriver")]
    public async Task<IActionResult> GetRoutesByDriver(Guid driverId, CancellationToken cancellationToken)
    {
        var routes = await routeQueryService.Handle(new GetRoutesByDriverQuery(OrganizationId, driverId),
            cancellationToken);
        return Ok(routes.Select(Assembler.ToResourceFromEntity));
    }

    [HttpPost("{driverId:guid}/verify")]
    [SwaggerOperation(Summary = "Verify a driver (US-01 S2)", OperationId = "VerifyDriver")]
    public async Task<IActionResult> VerifyDriver(Guid driverId, CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new VerifyDriverCommand(OrganizationId, driverId), cancellationToken));

    [HttpPost("{driverId:guid}/suspend")]
    [SwaggerOperation(Summary = "Suspend a driver", OperationId = "SuspendDriver")]
    public async Task<IActionResult> SuspendDriver(Guid driverId, CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new SuspendDriverCommand(OrganizationId, driverId), cancellationToken));

    [HttpPost("{driverId:guid}/reinstate")]
    [SwaggerOperation(Summary = "Reinstate a suspended driver", OperationId = "ReinstateDriver")]
    public async Task<IActionResult> ReinstateDriver(Guid driverId, CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new ReinstateDriverCommand(OrganizationId, driverId), cancellationToken));

    [HttpPut("{driverId:guid}/contact")]
    [SwaggerOperation(Summary = "Update the driver's phone number", OperationId = "UpdateDriverContact")]
    public async Task<IActionResult> UpdateDriverContact(Guid driverId, UpdateDriverContactResource resource,
        CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(
            new UpdateDriverContactCommand(OrganizationId, driverId, resource.PhoneNumber), cancellationToken));

    [HttpPut("{driverId:guid}/license")]
    [SwaggerOperation(Summary = "Renew the driver's license", OperationId = "RenewDriverLicense")]
    public async Task<IActionResult> RenewDriverLicense(Guid driverId, RenewDriverLicenseResource resource,
        CancellationToken cancellationToken) =>
        Respond(await commandService.Handle(new RenewDriverLicenseCommand(OrganizationId, driverId,
            resource.LicenseNumber, resource.LicenseCategory, resource.LicenseExpiresOn), cancellationToken));

    private IActionResult Respond(Result<Driver> result) =>
        AdministrationActionResultAssembler.ToActionResult(this, result, ProblemDetails,
            driver => Ok(Assembler.ToResourceFromEntity(driver)));
}
