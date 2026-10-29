using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Iam.Interfaces.Security;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Monolith.Subscription.Application.CommandServices;
using Monolith.Subscription.Application.QueryServices;
using Monolith.Subscription.Domain.Model;
using Monolith.Subscription.Domain.Model.Commands;
using Monolith.Subscription.Domain.Model.Queries;
using Monolith.Subscription.Interfaces.Rest.Resources;
using Monolith.Subscription.Interfaces.Rest.Transform;
using Swashbuckle.AspNetCore.Annotations;
using Assembler = Monolith.Subscription.Interfaces.Rest.Transform.SubscriptionResourceAssembler;

namespace Monolith.Subscription.Interfaces.Rest;

/// <summary>
///     Subscription (US-07): plan catalog, the contract of the organization and its plan changes. The organization and
///     the administrator always come from the Iam JWT, never from the request body.
/// </summary>
[ApiController]
[Route("api/v1/subscription")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(Policy = IamPolicies.OrganizationAdministrator)]
[SwaggerTag("Subscription: plans, the contract of the organization and plan changes paid through the gateway.")]
public class SubscriptionController(
    ISubscriptionCommandService commandService,
    ISubscriptionQueryService queryService,
    ProblemDetailsFactory problemDetailsFactory) : ControllerBase
{
    private Guid OrganizationId => User.GetOrganizationId() ?? Guid.Empty;

    private Guid CurrentUserId => User.GetUserId() ?? Guid.Empty;

    [HttpGet("plans")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Plans that can be contracted (public price list)", OperationId = "GetPlans")]
    public async Task<IActionResult> GetPlans(CancellationToken cancellationToken)
    {
        var plans = await queryService.Handle(new GetAvailablePlansQuery(), cancellationToken);
        return Ok(plans.Select(Assembler.ToResource));
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Contract of the organization: plan, limits in force and pending change",
        OperationId = "GetSubscription")]
    public async Task<IActionResult> GetSubscription(CancellationToken cancellationToken)
    {
        var view = await queryService.Handle(new GetSubscriptionQuery(OrganizationId), cancellationToken);
        return Ok(Assembler.ToResource(view));
    }

    /// <remarks>
    ///     202 with status PENDING_PAYMENT and the checkout of the gateway for a paid plan: its limits apply only after
    ///     the payment is verified. 200 with status APPLIED for the free plan.
    /// </remarks>
    [HttpPost("plan-changes")]
    [SwaggerOperation(Summary = "Request a plan change (US-07)", OperationId = "RequestPlanChange")]
    public async Task<IActionResult> RequestPlanChange(RequestPlanChangeResource resource,
        CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(
            new RequestPlanChangeCommand(OrganizationId, CurrentUserId, resource.TargetPlanId), cancellationToken);
        return SubscriptionActionResultAssembler.ToActionResult(this, result, problemDetailsFactory, change =>
            change.Status.IsPending
                ? AcceptedAtAction(nameof(GetPlanChange), new { planChangeId = change.Id }, Assembler.ToResource(change))
                : Ok(Assembler.ToResource(change)));
    }

    [HttpGet("plan-changes/{planChangeId:guid}")]
    [SwaggerOperation(Summary = "A plan change and the state of its payment", OperationId = "GetPlanChange")]
    public async Task<IActionResult> GetPlanChange(Guid planChangeId, CancellationToken cancellationToken)
    {
        var change = await queryService.Handle(new GetPlanChangeQuery(OrganizationId, planChangeId),
            cancellationToken);
        return change is null
            ? SubscriptionActionResultAssembler.Problem(this, problemDetailsFactory,
                SubscriptionError.PlanChangeNotFound, "The plan change does not exist.")
            : Ok(Assembler.ToResource(change));
    }

    [HttpPost("plan-changes/{planChangeId:guid}/cancel")]
    [SwaggerOperation(Summary = "Withdraw a plan change still waiting for its payment",
        OperationId = "CancelPlanChange")]
    public async Task<IActionResult> CancelPlanChange(Guid planChangeId, CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new CancelPlanChangeCommand(OrganizationId, planChangeId),
            cancellationToken);
        return SubscriptionActionResultAssembler.ToActionResult(this, result, problemDetailsFactory,
            change => Ok(Assembler.ToResource(change)));
    }

    /// <remarks>
    ///     Only with <c>Subscription:PaymentGateway:AllowSimulatedPayments</c> (development and demos). It produces the
    ///     same signed notification the gateway would send, so the payment is verified like a real one.
    /// </remarks>
    [HttpPost("sandbox/checkouts/{paymentReference}/complete")]
    [SwaggerOperation(Summary = "Sandbox: approve or decline a simulated checkout",
        OperationId = "CompleteSimulatedCheckout")]
    public async Task<IActionResult> CompleteSimulatedCheckout(string paymentReference,
        CompleteSimulatedCheckoutResource resource, CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(
            new CompleteSimulatedCheckoutCommand(OrganizationId, paymentReference, resource.Approved),
            cancellationToken);
        return SubscriptionActionResultAssembler.ToActionResult(this, result, problemDetailsFactory,
            receipt => Ok(Assembler.ToResource(receipt)));
    }
}
