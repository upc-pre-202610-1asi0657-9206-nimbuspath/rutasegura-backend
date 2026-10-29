using System.Net.Mime;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;
using Monolith.Subscription.Application.CommandServices;
using Monolith.Subscription.Domain.Model.Commands;
using Monolith.Subscription.Interfaces.Rest.Transform;
using Swashbuckle.AspNetCore.Annotations;

namespace Monolith.Subscription.Interfaces.Rest;

/// <summary>
///     Callback of the payment gateway (report, SubscriptionController: "the callback of the payment provider enters
///     through a specific route exposed by the API Gateway and is verified before being processed"). It has no JWT:
///     the signature of the body is what proves it comes from the gateway.
/// </summary>
/// <remarks>
///     200 for every verified notification, repeated ones included (the gateway stops retrying); 401 when the
///     signature is missing or wrong; 409 when it lost against a concurrent change and 503 when the payment could not
///     be read from the gateway (in both cases the gateway retries it).
/// </remarks>
[ApiController]
[Route("api/v1/subscription/payments/notifications")]
[AllowAnonymous]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Subscription: notifications of the payment gateway (signed webhook).")]
public class PaymentNotificationsController(
    ISubscriptionCommandService commandService,
    ProblemDetailsFactory problemDetailsFactory) : ControllerBase
{
    private const int MaxNotificationBytes = 64 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxNotificationBytes)]
    [SwaggerOperation(Summary = "Receive a payment notification of the gateway", OperationId = "ReceivePaymentNotification")]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        // The signature covers the exact bytes, so the body is read raw instead of being model-bound.
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        // Each provider signs different parts (Mercado Pago: x-signature, x-request-id and data.id of the query).
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
        var query = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var result = await commandService.Handle(new ProcessPaymentNotificationCommand(payload, headers, query),
            cancellationToken);
        return SubscriptionActionResultAssembler.ToActionResult(this, result, problemDetailsFactory,
            receipt => Ok(SubscriptionResourceAssembler.ToResource(receipt)));
    }
}
