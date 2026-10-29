using Monolith.Subscription.Domain.Model.Aggregates;
using Monolith.Subscription.Domain.Model.ValueObjects;

namespace RutaSegura.Monolith.Subscription.Tests.Domain;

public sealed class PlanAndValueObjectTests
{
    [Theory]
    [InlineData(" complete ", "COMPLETE")]
    [InlineData("plan-2026_b", "PLAN-2026_B")]
    public void PlanCode_IsNormalized(string raw, string expected) => new PlanCode(raw).Value.ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PLAN COMPLETO")]
    [InlineData("A_CODE_LONGER_THAN_TWENTY")]
    public void PlanCode_Invalid_IsRejected(string raw) => Should.Throw<ArgumentException>(() => new PlanCode(raw));

    [Fact]
    public void Money_KeepsCents_AndComparesByValue()
    {
        var price = Money.Soles(99.90m);

        price.AmountInCents.ShouldBe(9_990);
        price.Currency.ShouldBe("PEN");
        price.Amount.ShouldBe(99.90m);
        price.ShouldBe(new Money(9_990, "pen"));
        price.ShouldNotBe(new Money(9_990, "USD"));
    }

    [Theory]
    [InlineData(-1, "PEN")]
    [InlineData(100, "SOLES")]
    [InlineData(100, "")]
    public void Money_Invalid_IsRejected(long cents, string currency) =>
        Should.Throw<ArgumentException>(() => new Money(cents, currency));

    [Fact]
    public void DefaultPlan_MustBeFreeAndAvailable()
    {
        Should.Throw<ArgumentException>(() => new Plan(new PlanCode("BASIC"), "Básico", "", Money.Soles(10), 3, 3,
            isDefault: true));
        Should.Throw<ArgumentException>(() => new Plan(new PlanCode("BASIC"), "Básico", "", Money.Soles(0), 3, 3,
            isDefault: true, isAvailable: false));
    }

    [Fact]
    public void Plan_GrantsAtLeastOneRouteAndVehicle()
    {
        Should.Throw<ArgumentException>(() => new Plan(new PlanCode("X"), "X", "", Money.Soles(1), 0, 3, false));
        Should.Throw<ArgumentException>(() => new Plan(new PlanCode("X"), "X", "", Money.Soles(1), 3, 0, false));
    }

    [Fact]
    public void Plan_RequiresPaymentOnlyWhenItHasAPrice()
    {
        new Plan(new PlanCode("FREE"), "Gratis", "", Money.Soles(0), 1, 1, false).RequiresPayment.ShouldBeFalse();
        new Plan(new PlanCode("PAID"), "Pago", "", Money.Soles(1), 1, 1, false).RequiresPayment.ShouldBeTrue();
    }
}
