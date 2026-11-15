using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Application.CommandServices;
using Monolith.Iam.Application.QueryServices;
using Monolith.Iam.Domain.Model;
using Monolith.Iam.Domain.Model.Commands;
using Monolith.Iam.Domain.Model.Queries;
using Monolith.Iam.Infrastructure.Authorization;
using Monolith.Iam.Interfaces.Rest.Resources;
using Monolith.Iam.Interfaces.Rest.Transform;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Swashbuckle.AspNetCore.Annotations;

namespace Monolith.Iam.Interfaces.Rest;

[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Iam organization (tenant) endpoints.")]
public class OrganizationsController(
    IIamCommandService commandService,
    IIamQueryService queryService,
    ProblemDetailsFactory problemDetailsFactory) : ControllerBase
{
    /// <summary>
    ///     Creates an organization together with its single administrator. Platform only (header
    ///     <c>X-Provisioning-Key</c>): in production this happens when Subscription confirms the payment of a plan.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [RequireProvisioningKey]
    [SwaggerOperation(Summary = "Provision an organization and its administrator (platform only)",
        OperationId = "ProvisionOrganization")]
    public async Task<IActionResult> ProvisionOrganization(ProvisionOrganizationResource resource,
        CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new ProvisionOrganizationCommand(resource.Name,
            resource.Administrator.FirstName, resource.Administrator.LastName, resource.Administrator.Email,
            resource.Administrator.Password), cancellationToken);
        return IamActionResultAssembler.ToActionResult(this, result, problemDetailsFactory,
            provisioned => CreatedAtAction(nameof(GetOrganizationById),
                new { organizationId = provisioned.Organization.Id.Identifier },
                new ProvisionedOrganizationResource(
                    IamResourceFromEntityAssembler.ToResourceFromEntity(provisioned.Organization),
                    IamResourceFromEntityAssembler.ToResourceFromEntity(provisioned.Administrator))));
    }

    /// <summary>Members of an organization can read it; other organizations are reported as not found.</summary>
    [HttpGet("{organizationId:guid}")]
    [Authorize(Policy = IamPolicies.Account)]
    [SwaggerOperation(Summary = "Get my organization", OperationId = "GetOrganizationById")]
    public async Task<IActionResult> GetOrganizationById(Guid organizationId, CancellationToken cancellationToken)
    {
        if (User.GetOrganizationId() != organizationId) return OrganizationNotFound();
        var organization = await queryService.Handle(new GetOrganizationByIdQuery(organizationId),
            cancellationToken);
        return organization is null
            ? OrganizationNotFound()
            : Ok(IamResourceFromEntityAssembler.ToResourceFromEntity(organization));
    }

    /// <summary>Only the administrator of the organization can rename it.</summary>
    [HttpPut("{organizationId:guid}")]
    [Authorize(Policy = IamPolicies.OrganizationAdministrator)]
    [SwaggerOperation(Summary = "Update my organization", OperationId = "UpdateOrganization")]
    public async Task<IActionResult> UpdateOrganization(Guid organizationId, UpdateOrganizationResource resource,
        CancellationToken cancellationToken)
    {
        if (User.GetOrganizationId() != organizationId) return OrganizationNotFound();
        var result = await commandService.Handle(new UpdateOrganizationCommand(organizationId, resource.Name),
            cancellationToken);
        return IamActionResultAssembler.ToActionResult(this, result, problemDetailsFactory,
            organization => Ok(IamResourceFromEntityAssembler.ToResourceFromEntity(organization)));
    }

    private IActionResult OrganizationNotFound() =>
        IamActionResultAssembler.Problem(this, problemDetailsFactory, IamError.OrganizationNotFound,
            "Organization was not found.");
}
