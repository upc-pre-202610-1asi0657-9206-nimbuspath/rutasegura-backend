using Monolith.Subscription.Domain.Model.ValueObjects;

namespace Monolith.Subscription.Domain.Model.Aggregates;

/// <summary>
///     A plan of the B2B catalog: its monthly price and the operational limits it grants to a transport company
///     (active routes and vehicles, US-07). The default plan is free and applies to every organization that has not
///     contracted another one.
/// </summary>
public class Plan
{
    public const int NameMaxLength = 60;
    public const int DescriptionMaxLength = 300;

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected Plan()
    {
        Code = new PlanCode();
        Currency = Money.DefaultCurrency;
        Name = string.Empty;
        Description = string.Empty;
    }

    public Plan(PlanCode code, string name, string description, Money monthlyPrice, int maxActiveRoutes,
        int maxVehicles, bool isDefault, bool isAvailable = true)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        ArgumentNullException.ThrowIfNull(monthlyPrice);
        MonthlyPriceInCents = monthlyPrice.AmountInCents;
        Currency = monthlyPrice.Currency;
        Name = Required(name, NameMaxLength, nameof(name));
        Description = (description ?? string.Empty).Trim();
        if (Description.Length > DescriptionMaxLength)
            throw new ArgumentException($"The description cannot exceed {DescriptionMaxLength} characters.",
                nameof(description));
        if (maxActiveRoutes < 1)
            throw new ArgumentException("A plan grants at least one active route.", nameof(maxActiveRoutes));
        if (maxVehicles < 1)
            throw new ArgumentException("A plan grants at least one vehicle.", nameof(maxVehicles));
        if (isDefault && !monthlyPrice.IsZero)
            throw new ArgumentException("The default plan is the free plan every organization starts with.",
                nameof(isDefault));
        if (isDefault && !isAvailable)
            throw new ArgumentException("The default plan cannot be withdrawn from the catalog.", nameof(isAvailable));

        MaxActiveRoutes = maxActiveRoutes;
        MaxVehicles = maxVehicles;
        IsDefault = isDefault;
        IsAvailable = isAvailable;
    }

    public PlanCode Code { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    /// <summary>Monthly price in the minor unit (persisted as two columns so the catalog can be seeded by migrations).</summary>
    public long MonthlyPriceInCents { get; private set; }

    public string Currency { get; private set; }

    public Money MonthlyPrice => new(MonthlyPriceInCents, Currency);

    public int MaxActiveRoutes { get; private set; }

    public int MaxVehicles { get; private set; }

    /// <summary>The free plan of organizations without a paid contract (exactly one in the catalog).</summary>
    public bool IsDefault { get; private set; }

    /// <summary>Whether new contracts can choose it. Withdrawn plans keep working for those who already have them.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Changing to a paid plan goes through the payment gateway; a free plan applies at once.</summary>
    public bool RequiresPayment => !MonthlyPrice.IsZero;

    private static string Required(string value, int maxLength, string name)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) throw new ArgumentException("The value is required.", name);
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"The value cannot exceed {maxLength} characters.", name);
        return trimmed;
    }
}
