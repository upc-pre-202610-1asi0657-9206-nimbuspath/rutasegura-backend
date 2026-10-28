using Monolith.Subscription.Domain.Model;
using Monolith.Subscription.Domain.Model.Aggregates;
using Monolith.Subscription.Domain.Model.Exceptions;
using Monolith.Subscription.Domain.Model.ValueObjects;
using static RutaSegura.Monolith.Subscription.Tests.Support.SubscriptionMother;

namespace RutaSegura.Monolith.Subscription.Tests.Domain;

/// <summary>Rules of the contract of a transport company (US-07, ADR-05.4).</summary>
public sealed class TenantSubscriptionTests
{
    private static readonly PlanCode BasicCode = new("BASIC");

    [Fact]
    public void NewSubscription_StartsOnTheDefaultFreePlan()
    {
        var subscription = NewSubscription();

        subscription.PlanCode.ShouldBe(BasicCode);
        subscription.PaidUntil.ShouldBeNull();
        subscription.PendingChange.ShouldBeNull();
        subscription.EffectivePlanCode(BasicCode, Now).ShouldBe(BasicCode);
    }

    [Fact]
    public void NewSubscription_OnAPaidPlan_IsRejected()
    {
        Should.Throw<ArgumentException>(() =>
            new TenantSubscription(new(Organization), Complete(), Now));
    }

    // US-07 S1: the change waits for its payment and the limits in force are kept.
    [Fact]
    public void RequestPaidPlan_StaysPendingAndKeepsTheCurrentPlan()
    {
        var subscription = NewSubscription();

        var change = subscription.RequestPlanChange(Complete(), BasicCode, Administrator, Now);

        change.Status.Value.ShouldBe(PlanChangeStatus.PendingPayment);
        change.Price.ShouldBe(new Money(9_990, "PEN"));
        change.PreviousPlanCode.ShouldBe(BasicCode);
        subscription.PendingChange.ShouldBe(change);
        subscription.EffectivePlanCode(BasicCode, Now).ShouldBe(BasicCode);
    }

    // US-07 S2: a valid confirmation activates the plan and its limits once.
    [Fact]
    public void ConfirmPayment_AppliesThePlanForOneMonth()
    {
        var subscription = WithCheckout(out var reference);

        var resolution = subscription.ConfirmPayment(reference, new Money(9_990, "PEN"), Now.AddMinutes(5));

        resolution.ShouldBe(PaymentResolution.Applied);
        subscription.PlanCode.ShouldBe(new PlanCode("COMPLETE"));
        subscription.PaidUntil.ShouldBe(Now.AddMinutes(5).AddMonths(1));
        subscription.PendingChange.ShouldBeNull();
        subscription.PlanChanges.Single().Status.Value.ShouldBe(PlanChangeStatus.Applied);
    }

    // US-07 S3: a repeated confirmation does not apply the change twice.
    [Fact]
    public void ConfirmPayment_Twice_IsIdempotent()
    {
        var subscription = WithCheckout(out var reference);
        subscription.ConfirmPayment(reference, new Money(9_990, "PEN"), Now);
        var paidUntil = subscription.PaidUntil;
        var version = subscription.Version;

        var again = subscription.ConfirmPayment(reference, new Money(9_990, "PEN"), Now.AddDays(3));

        again.ShouldBe(PaymentResolution.AlreadyApplied);
        subscription.PaidUntil.ShouldBe(paidUntil);
        subscription.Version.ShouldBe(version);
    }

    // US-07 S3: a declined payment keeps the plan in force.
    [Fact]
    public void RejectPayment_KeepsTheCurrentPlan()
    {
        var subscription = WithCheckout(out var reference);

        var resolution = subscription.RejectPayment(reference, "Tarjeta sin fondos", Now);

        resolution.ShouldBe(PaymentResolution.Rejected);
        subscription.PlanCode.ShouldBe(BasicCode);
        subscription.PendingChange.ShouldBeNull();
        subscription.PlanChanges.Single().ResolutionDetail.ShouldBe("Tarjeta sin fondos");
    }

    // US-07 S3: a late confirmation after a rejection never activates the change.
    [Fact]
    public void ConfirmPayment_AfterRejection_DoesNotActivate()
    {
        var subscription = WithCheckout(out var reference);
        subscription.RejectPayment(reference, null, Now);

        var late = subscription.ConfirmPayment(reference, new Money(9_990, "PEN"), Now.AddHours(1));

        late.ShouldBe(PaymentResolution.AlreadyResolved);
        subscription.PlanCode.ShouldBe(BasicCode);
    }

    [Fact]
    public void ConfirmPayment_WithAnotherAmount_RejectsTheChange()
    {
        var subscription = WithCheckout(out var reference);

        var resolution = subscription.ConfirmPayment(reference, new Money(100, "PEN"), Now);

        resolution.ShouldBe(PaymentResolution.AmountMismatch);
        subscription.PlanCode.ShouldBe(BasicCode);
        subscription.PlanChanges.Single().Status.Value.ShouldBe(PlanChangeStatus.Rejected);
    }

    [Fact]
    public void ConfirmPayment_InAnotherCurrency_RejectsTheChange()
    {
        var subscription = WithCheckout(out var reference);

        subscription.ConfirmPayment(reference, new Money(9_990, "USD"), Now).ShouldBe(PaymentResolution.AmountMismatch);
    }

    [Fact]
    public void RequestChange_WhileAnotherIsPending_IsRejected()
    {
        var subscription = NewSubscription();
        subscription.RequestPlanChange(Complete(), BasicCode, Administrator, Now);

        var error = Should.Throw<SubscriptionDomainException>(() =>
            subscription.RequestPlanChange(Premium(), BasicCode, Administrator, Now));

        error.Error.ShouldBe(SubscriptionError.PlanChangeAlreadyPending);
    }

    [Fact]
    public void RequestChange_ToTheCurrentPlan_IsRejected()
    {
        var error = Should.Throw<SubscriptionDomainException>(() =>
            NewSubscription().RequestPlanChange(Basic(), BasicCode, Administrator, Now));

        error.Error.ShouldBe(SubscriptionError.PlanAlreadyContracted);
    }

    [Fact]
    public void RequestChange_ToAWithdrawnPlan_IsRejected()
    {
        var error = Should.Throw<SubscriptionDomainException>(() =>
            NewSubscription().RequestPlanChange(Withdrawn(), BasicCode, Administrator, Now));

        error.Error.ShouldBe(SubscriptionError.PlanNotAvailable);
    }

    [Fact]
    public void RequestFreePlan_AppliesAtOnceWithoutPayment()
    {
        var subscription = WithCheckout(out var reference);
        subscription.ConfirmPayment(reference, new Money(9_990, "PEN"), Now);

        var downgrade = subscription.RequestPlanChange(Basic(), BasicCode, Administrator, Now.AddDays(10));

        downgrade.Status.Value.ShouldBe(PlanChangeStatus.Applied);
        downgrade.PaymentReference.ShouldBeNull();
        subscription.PlanCode.ShouldBe(BasicCode);
        subscription.PaidUntil.ShouldBeNull();
    }

    [Fact]
    public void PaidPeriodOver_FallsBackToTheDefaultPlanLimits()
    {
        var subscription = WithCheckout(out var reference);
        subscription.ConfirmPayment(reference, new Money(9_990, "PEN"), Now);

        subscription.EffectivePlanCode(BasicCode, Now.AddMonths(1).AddMinutes(-1)).ShouldBe(new PlanCode("COMPLETE"));
        subscription.EffectivePlanCode(BasicCode, Now.AddMonths(1)).ShouldBe(BasicCode);
    }

    [Fact]
    public void CancelPendingChange_EndsIt_AndAllowsANewOne()
    {
        var subscription = NewSubscription();
        var change = subscription.RequestPlanChange(Complete(), BasicCode, Administrator, Now);

        subscription.CancelPlanChange(change.Id, "Cambio de opinión", Now);

        change.Status.Value.ShouldBe(PlanChangeStatus.Cancelled);
        subscription.RequestPlanChange(Premium(), BasicCode, Administrator, Now).Status.IsPending.ShouldBeTrue();
    }

    [Fact]
    public void CancelAFinalChange_IsRejected()
    {
        var subscription = WithCheckout(out var reference);
        subscription.ConfirmPayment(reference, new Money(9_990, "PEN"), Now);

        var error = Should.Throw<SubscriptionDomainException>(() =>
            subscription.CancelPlanChange(subscription.PlanChanges.Single().Id, "tarde", Now));

        error.Error.ShouldBe(SubscriptionError.PlanChangeNotPending);
    }

    [Fact]
    public void EveryChangeOfTheContract_IncrementsItsVersion()
    {
        var subscription = NewSubscription();
        var change = subscription.RequestPlanChange(Complete(), BasicCode, Administrator, Now);
        var afterRequest = subscription.Version;
        subscription.AttachCheckout(change.Id, "sbx_1", null);

        afterRequest.ShouldBe(1);
        subscription.Version.ShouldBe(2);
    }

    private static TenantSubscription WithCheckout(out string reference)
    {
        var subscription = NewSubscription();
        var change = subscription.RequestPlanChange(Complete(), BasicCode, Administrator, Now);
        reference = "sbx_" + change.Id.ToString("N");
        subscription.AttachCheckout(change.Id, reference, null);
        return subscription;
    }
}
