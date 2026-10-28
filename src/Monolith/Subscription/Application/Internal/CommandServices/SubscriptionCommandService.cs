using Monolith.Shared.Application.Model;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Subscription.Application.CommandServices;
using Monolith.Subscription.Application.Internal.OutboundServices;
using Monolith.Subscription.Domain.Model;
using Monolith.Subscription.Domain.Model.Aggregates;
using Monolith.Subscription.Domain.Model.Commands;
using Monolith.Subscription.Domain.Model.Entities;
using Monolith.Subscription.Domain.Model.ValueObjects;
using Monolith.Subscription.Domain.Repositories;

namespace Monolith.Subscription.Application.Internal.CommandServices;

/// <remarks>
///     ADR-05.4: the plan change is saved before the gateway is called and the checkout reference after it, so no
///     database transaction stays open while the provider answers. If the checkout cannot be created the change is
///     cancelled and the administrator can try again. Every notification is recorded with its event id in the same
///     save as the change of the contract, so a retry of the gateway finds it and does nothing.
/// </remarks>
public class SubscriptionCommandService(
    IPlanRepository planRepository,
    ITenantSubscriptionRepository subscriptionRepository,
    IProcessedPaymentEventRepository paymentEventRepository,
    IPaymentGateway paymentGateway,
    ISimulatedPaymentNotifier simulatedPayments,
    ISubscriptionUnitOfWork unitOfWork,
    TimeProvider clock) : ISubscriptionCommandService
{
    public const string DuplicateResolution = "DUPLICATE";
    public const string UnmatchedResolution = "UNMATCHED";

    public Task<Result<PlanChange>> Handle(RequestPlanChangeCommand command, CancellationToken cancellationToken) =>
        SubscriptionOperation.ExecuteAsync<PlanChange>(async () =>
        {
            var organizationId = RequireOrganization(command.OrganizationId);
            var target = await planRepository.FindByCodeAsync(new PlanCode(command.TargetPlanCode), cancellationToken);
            if (target is null)
                return Result<PlanChange>.Failure(SubscriptionError.PlanNotFound,
                    $"The plan {command.TargetPlanCode} does not exist.");
            var defaultPlan = await planRepository.GetDefaultAsync(cancellationToken);

            var subscription = await subscriptionRepository.FindByOrganizationAsync(organizationId, cancellationToken);
            if (subscription is null)
            {
                subscription = new TenantSubscription(organizationId, defaultPlan, clock.GetUtcNow());
                await subscriptionRepository.AddAsync(subscription, cancellationToken);
            }
            else
            {
                subscriptionRepository.Update(subscription);
            }

            var change = subscription.RequestPlanChange(target, defaultPlan.Code, command.RequestedBy,
                clock.GetUtcNow());
            if (!await unitOfWork.TryCompleteAsync(cancellationToken)) return Conflict<PlanChange>();
            if (!change.Status.IsPending) return Result<PlanChange>.Success(change);

            // Outside any transaction: the provider may be slow or down.
            PaymentCheckout checkout;
            try
            {
                checkout = await paymentGateway.CreateCheckoutAsync(new PaymentCheckoutRequest(change.Id,
                    organizationId.Identifier, target.Code.Value, target.Name, change.Price.AmountInCents,
                    change.Price.Currency), cancellationToken);
            }
            catch (PaymentGatewayUnavailableException ex)
            {
                subscription.CancelPlanChange(change.Id, $"The checkout could not be started: {ex.Message}",
                    clock.GetUtcNow());
                subscriptionRepository.Update(subscription);
                await unitOfWork.TryCompleteAsync(cancellationToken);
                return Result<PlanChange>.Failure(SubscriptionError.PaymentGatewayUnavailable,
                    "The payment gateway is not available. The plan was not changed; try again later.");
            }

            subscription.AttachCheckout(change.Id, checkout.Reference, checkout.CheckoutUrl);
            subscriptionRepository.Update(subscription);
            return await unitOfWork.TryCompleteAsync(cancellationToken)
                ? Result<PlanChange>.Success(change)
                : Conflict<PlanChange>();
        });

    public Task<Result<PlanChange>> Handle(CancelPlanChangeCommand command, CancellationToken cancellationToken) =>
        SubscriptionOperation.ExecuteAsync<PlanChange>(async () =>
        {
            var subscription = await subscriptionRepository.FindByOrganizationAsync(
                RequireOrganization(command.OrganizationId), cancellationToken);
            if (subscription?.FindPlanChange(command.PlanChangeId) is null) return PlanChangeNotFound<PlanChange>();

            var change = subscription.CancelPlanChange(command.PlanChangeId, "Cancelled by the administrator.",
                clock.GetUtcNow());
            subscriptionRepository.Update(subscription);
            return await unitOfWork.TryCompleteAsync(cancellationToken)
                ? Result<PlanChange>.Success(change)
                : Conflict<PlanChange>();
        });

    public Task<Result<PaymentNotificationReceipt>> Handle(ProcessPaymentNotificationCommand command,
        CancellationToken cancellationToken) =>
        SubscriptionOperation.ExecuteAsync<PaymentNotificationReceipt>(async () =>
        {
            var notification = paymentGateway.ReadNotification(command.Payload ?? string.Empty, command.Signature);
            if (notification is null)
                return Result<PaymentNotificationReceipt>.Failure(SubscriptionError.PaymentNotificationInvalid,
                    "The notification is not signed by the payment gateway.");
            return await ApplyAsync(notification, cancellationToken);
        });

    public Task<Result<PaymentNotificationReceipt>> Handle(CompleteSimulatedCheckoutCommand command,
        CancellationToken cancellationToken) =>
        SubscriptionOperation.ExecuteAsync<PaymentNotificationReceipt>(async () =>
        {
            if (!simulatedPayments.Enabled)
                return Result<PaymentNotificationReceipt>.Failure(SubscriptionError.SimulatedPaymentsDisabled,
                    "Simulated payments are disabled in this environment.");

            var subscription = await subscriptionRepository.FindByOrganizationAsync(
                RequireOrganization(command.OrganizationId), cancellationToken);
            var change = subscription?.PlanChanges.SingleOrDefault(c => c.PaymentReference == command.PaymentReference);
            if (change is null) return PlanChangeNotFound<PaymentNotificationReceipt>();

            // The same signed notification a gateway would send: verified and deduplicated like a real one.
            var signed = simulatedPayments.Create(command.PaymentReference, command.Approved,
                change.Price.AmountInCents, change.Price.Currency);
            return await Handle(new ProcessPaymentNotificationCommand(signed.Payload, signed.Signature),
                cancellationToken);
        });

    private async Task<Result<PaymentNotificationReceipt>> ApplyAsync(PaymentNotification notification,
        CancellationToken cancellationToken)
    {
        if (await paymentEventRepository.ExistsAsync(notification.EventId, cancellationToken))
            return Result<PaymentNotificationReceipt>.Success(
                new PaymentNotificationReceipt(notification.EventId, DuplicateResolution, null, null));

        var now = clock.GetUtcNow();
        var outcome = SubscriptionOperation.ToCode(notification.Outcome);
        var subscription =
            await subscriptionRepository.FindByPaymentReferenceAsync(notification.PaymentReference, cancellationToken);
        if (subscription is null)
        {
            // Acknowledged (the gateway stops retrying) and kept for review: no contract has that checkout.
            await paymentEventRepository.AddAsync(new ProcessedPaymentEvent(notification.EventId,
                notification.PaymentReference, outcome, UnmatchedResolution, null, now), cancellationToken);
            return await unitOfWork.TryCompleteAsync(cancellationToken)
                ? Result<PaymentNotificationReceipt>.Success(
                    new PaymentNotificationReceipt(notification.EventId, UnmatchedResolution, null, null))
                : Conflict<PaymentNotificationReceipt>();
        }

        var resolution = notification.Outcome == PaymentOutcome.Approved
            ? subscription.ConfirmPayment(notification.PaymentReference,
                new Money(notification.AmountInCents, notification.Currency), now)
            : subscription.RejectPayment(notification.PaymentReference, notification.Reason, now);
        var resolutionCode = SubscriptionOperation.ToCode(resolution);

        subscriptionRepository.Update(subscription);
        await paymentEventRepository.AddAsync(new ProcessedPaymentEvent(notification.EventId,
            notification.PaymentReference, outcome, resolutionCode, subscription.OrganizationId.Identifier, now),
            cancellationToken);
        if (!await unitOfWork.TryCompleteAsync(cancellationToken)) return Conflict<PaymentNotificationReceipt>();

        var change = subscription.PlanChanges.Single(c => c.PaymentReference == notification.PaymentReference);
        return Result<PaymentNotificationReceipt>.Success(new PaymentNotificationReceipt(notification.EventId,
            resolutionCode, change.Id, change.Status.Value));
    }

    private static OrganizationId RequireOrganization(Guid organizationId) =>
        organizationId == Guid.Empty
            ? throw new ArgumentException("The organization is required.", nameof(organizationId))
            : new OrganizationId(organizationId);

    private static Result<T> PlanChangeNotFound<T>() =>
        Result<T>.Failure(SubscriptionError.PlanChangeNotFound, "The plan change does not exist.");

    private static Result<T> Conflict<T>() =>
        Result<T>.Failure(SubscriptionError.ConcurrencyConflict,
            "The subscription was changed by another request at the same time. Try again.");
}
