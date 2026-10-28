using Monolith.Shared.Application.Model;
using Monolith.Subscription.Application.CommandServices;
using Monolith.Subscription.Application.Internal.CommandServices;
using Monolith.Subscription.Application.QueryServices;
using Monolith.Subscription.Domain.Model;
using Monolith.Subscription.Domain.Model.Commands;
using Monolith.Subscription.Domain.Model.Entities;
using Monolith.Subscription.Domain.Model.Queries;
using Monolith.Subscription.Domain.Model.ValueObjects;
using RutaSegura.Monolith.Subscription.Tests.Support;
using static RutaSegura.Monolith.Subscription.Tests.Support.SubscriptionMother;

namespace RutaSegura.Monolith.Subscription.Tests.Application;

/// <summary>Use cases of US-07 with in-memory doubles of the ports and the real sandbox gateway.</summary>
public sealed class SubscriptionCommandServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly SubscriptionFixture _fixture = new();

    [Fact]
    public async Task OrganizationWithoutContract_GetsTheDefaultPlanLimits()
    {
        var limits = await _fixture.Queries.Handle(new GetEffectiveLimitsQuery(Organization), Ct);

        limits.ShouldBe(new("BASIC", 3, 3));
    }

    [Fact]
    public async Task RequestPaidPlan_CreatesTheCheckout_AndKeepsTheLimitsInForce()
    {
        var result = await RequestAsync("complete");

        result.IsSuccess.ShouldBeTrue();
        var change = result.Value!;
        change.Status.Value.ShouldBe(PlanChangeStatus.PendingPayment);
        change.PaymentReference.ShouldStartWith("sbx_");
        change.CheckoutUrl.ShouldBe($"https://rutasegura.test/checkout/{change.PaymentReference}");
        var request = _fixture.Gateway.Requests.ShouldHaveSingleItem();
        request.PlanCode.ShouldBe("COMPLETE");
        request.AmountInCents.ShouldBe(9_990);
        request.Currency.ShouldBe("PEN");
        request.OrganizationId.ShouldBe(Organization);
        (await LimitsAsync()).PlanCode.ShouldBe("BASIC");
        // Saved before calling the gateway and again with its checkout (ADR-05.4: no transaction open meanwhile).
        _fixture.UnitOfWork.Saves.ShouldBe(2);
    }

    [Fact]
    public async Task RequestUnknownPlan_IsNotFound()
    {
        var result = await RequestAsync("GOLD");

        result.Error.ShouldBe(SubscriptionError.PlanNotFound);
        _fixture.Gateway.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task RequestFreePlan_AppliesWithoutCallingTheGateway()
    {
        await PayAsync(await PendingReferenceAsync("PREMIUM"));

        var result = await RequestAsync("BASIC");

        result.Value!.Status.Value.ShouldBe(PlanChangeStatus.Applied);
        _fixture.Gateway.Requests.Count.ShouldBe(1);
        (await LimitsAsync()).PlanCode.ShouldBe("BASIC");
    }

    [Fact]
    public async Task GatewayDown_CancelsTheChange_AndTheAdministratorCanRetry()
    {
        _fixture.Gateway.Down = true;

        var failed = await RequestAsync("COMPLETE");

        failed.Error.ShouldBe(SubscriptionError.PaymentGatewayUnavailable);
        var subscription = _fixture.Subscriptions.Saved.ShouldHaveSingleItem();
        subscription.PlanChanges.Single().Status.Value.ShouldBe(PlanChangeStatus.Cancelled);
        subscription.PendingChange.ShouldBeNull();

        _fixture.Gateway.Down = false;
        (await RequestAsync("COMPLETE")).Value!.Status.IsPending.ShouldBeTrue();
    }

    [Fact]
    public async Task ConcurrentSaveLost_IsReportedAsConflict()
    {
        _fixture.UnitOfWork.NextResults.Enqueue(false);

        var result = await RequestAsync("COMPLETE");

        result.Error.ShouldBe(SubscriptionError.ConcurrencyConflict);
        _fixture.Gateway.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApprovedNotification_AppliesThePlanAndItsLimits()
    {
        var reference = await PendingReferenceAsync("COMPLETE");

        var receipt = await PayAsync(reference);

        receipt.Resolution.ShouldBe("APPLIED");
        receipt.PlanChangeStatus.ShouldBe(PlanChangeStatus.Applied);
        (await LimitsAsync()).ShouldBe(new("COMPLETE", 10, 10));
        var recorded = _fixture.PaymentEvents.Saved.ShouldHaveSingleItem();
        recorded.Outcome.ShouldBe("APPROVED");
        recorded.OrganizationId.ShouldBe(Organization);
    }

    [Fact]
    public async Task SameNotificationTwice_IsProcessedOnce()
    {
        var reference = await PendingReferenceAsync("COMPLETE");
        var notification = _fixture.Sandbox.Create(reference, true, 9_990, "PEN");
        var command = new ProcessPaymentNotificationCommand(notification.Payload, notification.Signature);

        (await _fixture.Commands.Handle(command, Ct)).Value!.Resolution.ShouldBe("APPLIED");
        var retry = await _fixture.Commands.Handle(command, Ct);

        retry.Value!.Resolution.ShouldBe(SubscriptionCommandService.DuplicateResolution);
        _fixture.PaymentEvents.Saved.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AnotherConfirmationOfAnAppliedChange_DoesNotApplyItTwice()
    {
        var reference = await PendingReferenceAsync("COMPLETE");
        await PayAsync(reference);
        var paidUntil = _fixture.Subscriptions.Saved.Single().PaidUntil;
        _fixture.Clock.Advance(TimeSpan.FromDays(2));

        var second = await PayAsync(reference);

        second.Resolution.ShouldBe("ALREADY_APPLIED");
        _fixture.Subscriptions.Saved.Single().PaidUntil.ShouldBe(paidUntil);
    }

    [Fact]
    public async Task RejectedNotification_KeepsTheLimitsInForce()
    {
        var reference = await PendingReferenceAsync("COMPLETE");

        var receipt = await PayAsync(reference, approved: false);

        receipt.Resolution.ShouldBe("REJECTED");
        (await LimitsAsync()).PlanCode.ShouldBe("BASIC");
        (await RequestAsync("COMPLETE")).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task NotificationWithAWrongSignature_ChangesNothing()
    {
        var reference = await PendingReferenceAsync("COMPLETE");
        var forged = SubscriptionMother.Sandbox(secret: "another-secret-another-secret-123")
            .Create(reference, true, 9_990, "PEN");

        var result = await _fixture.Commands.Handle(
            new ProcessPaymentNotificationCommand(forged.Payload, forged.Signature), Ct);

        result.Error.ShouldBe(SubscriptionError.PaymentNotificationInvalid);
        _fixture.PaymentEvents.Saved.ShouldBeEmpty();
        (await LimitsAsync()).PlanCode.ShouldBe("BASIC");
    }

    [Fact]
    public async Task TamperedAmount_BreaksTheSignature()
    {
        var reference = await PendingReferenceAsync("COMPLETE");
        var signed = _fixture.Sandbox.Create(reference, true, 9_990, "PEN");

        var result = await _fixture.Commands.Handle(new ProcessPaymentNotificationCommand(
            signed.Payload.Replace("9990", "1"), signed.Signature), Ct);

        result.Error.ShouldBe(SubscriptionError.PaymentNotificationInvalid);
    }

    [Fact]
    public async Task NotificationOfAnUnknownCheckout_IsAcknowledgedAndKept()
    {
        var receipt = await PayAsync("sbx_unknown");

        receipt.Resolution.ShouldBe(SubscriptionCommandService.UnmatchedResolution);
        _fixture.PaymentEvents.Saved.ShouldHaveSingleItem().OrganizationId.ShouldBeNull();
    }

    [Fact]
    public async Task CancelPendingChange_AllowsAnotherRequest()
    {
        var change = (await RequestAsync("COMPLETE")).Value!;

        var cancelled = await _fixture.Commands.Handle(new CancelPlanChangeCommand(Organization, change.Id), Ct);

        cancelled.Value!.Status.Value.ShouldBe(PlanChangeStatus.Cancelled);
        (await RequestAsync("PREMIUM")).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task CancelChangeOfAnotherOrganization_IsNotFound()
    {
        var change = (await RequestAsync("COMPLETE")).Value!;

        var result = await _fixture.Commands.Handle(new CancelPlanChangeCommand(Guid.NewGuid(), change.Id), Ct);

        result.Error.ShouldBe(SubscriptionError.PlanChangeNotFound);
    }

    [Fact]
    public async Task SimulatedCheckout_GoesThroughTheSameVerification()
    {
        var reference = await PendingReferenceAsync("PREMIUM");

        var receipt = await _fixture.Commands.Handle(
            new CompleteSimulatedCheckoutCommand(Organization, reference, Approved: true), Ct);

        receipt.Value!.Resolution.ShouldBe("APPLIED");
        (await LimitsAsync()).PlanCode.ShouldBe("PREMIUM");
    }

    [Fact]
    public async Task SimulatedCheckoutOfAnotherOrganization_IsNotFound()
    {
        var reference = await PendingReferenceAsync("PREMIUM");

        var result = await _fixture.Commands.Handle(
            new CompleteSimulatedCheckoutCommand(Guid.NewGuid(), reference, Approved: true), Ct);

        result.Error.ShouldBe(SubscriptionError.PlanChangeNotFound);
    }

    [Fact]
    public async Task SimulatedPaymentsDisabled_AreRefused()
    {
        var fixture = new SubscriptionFixture(allowSimulatedPayments: false);

        var result = await fixture.Commands.Handle(
            new CompleteSimulatedCheckoutCommand(Organization, "sbx_x", Approved: true), Ct);

        result.Error.ShouldBe(SubscriptionError.SimulatedPaymentsDisabled);
    }

    [Fact]
    public async Task SubscriptionView_ShowsContractedEffectiveAndPendingChange()
    {
        var reference = await PendingReferenceAsync("COMPLETE");
        await PayAsync(reference);
        await PendingReferenceAsync("PREMIUM");

        var view = await _fixture.Queries.Handle(new GetSubscriptionQuery(Organization), Ct);

        view.ContractedPlan.Code.Value.ShouldBe("COMPLETE");
        view.EffectivePlan.Code.Value.ShouldBe("COMPLETE");
        view.PendingChange!.TargetPlanCode.Value.ShouldBe("PREMIUM");

        _fixture.Clock.Advance(TimeSpan.FromDays(40));
        var expired = await _fixture.Queries.Handle(new GetSubscriptionQuery(Organization), Ct);
        expired.ContractedPlan.Code.Value.ShouldBe("COMPLETE");
        expired.EffectivePlan.Code.Value.ShouldBe("BASIC");
    }

    [Fact]
    public async Task AvailablePlans_ExcludeWithdrawnOnes_CheapestFirst()
    {
        var plans = await _fixture.Queries.Handle(new GetAvailablePlansQuery(), Ct);

        plans.Select(p => p.Code.Value).ShouldBe(["BASIC", "COMPLETE", "PREMIUM"]);
    }

    private Task<Result<PlanChange>> RequestAsync(string plan) =>
        _fixture.Commands.Handle(new RequestPlanChangeCommand(Organization, Administrator, plan), Ct);

    private async Task<string> PendingReferenceAsync(string plan) =>
        (await RequestAsync(plan)).Value!.PaymentReference!;

    private async Task<PaymentNotificationReceipt> PayAsync(string reference, bool approved = true)
    {
        var change = _fixture.Subscriptions.Saved.SelectMany(s => s.PlanChanges)
            .SingleOrDefault(c => c.PaymentReference == reference);
        var signed = _fixture.Sandbox.Create(reference, approved, change?.Price.AmountInCents ?? 9_990,
            change?.Price.Currency ?? "PEN");
        var result = await _fixture.Commands.Handle(
            new ProcessPaymentNotificationCommand(signed.Payload, signed.Signature), Ct);
        result.IsSuccess.ShouldBeTrue(result.Message);
        return result.Value!;
    }

    private Task<EffectivePlanLimits> LimitsAsync() =>
        _fixture.Queries.Handle(new GetEffectiveLimitsQuery(Organization), Ct);
}
