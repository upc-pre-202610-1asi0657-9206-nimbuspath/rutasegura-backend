namespace Monolith.Subscription.Domain.Model.ValueObjects;

/// <summary>
///     An amount of money in the minor unit of its currency (céntimos for PEN), so prices are exact and every payment
///     gateway can receive them without rounding. The default currency is the Peruvian sol (C-16: Peruvian operators).
/// </summary>
public record Money
{
    public const string DefaultCurrency = "PEN";

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    public Money()
    {
        Currency = DefaultCurrency;
    }

    public Money(long amountInCents, string currency)
    {
        if (amountInCents < 0)
            throw new ArgumentException("An amount of money cannot be negative.", nameof(amountInCents));
        var normalized = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsAsciiLetterUpper))
            throw new ArgumentException("The currency must be an ISO 4217 code (e.g. PEN).", nameof(currency));
        AmountInCents = amountInCents;
        Currency = normalized;
    }

    public long AmountInCents { get; init; }

    public string Currency { get; init; }

    public bool IsZero => AmountInCents == 0;

    /// <summary>Amount in the major unit (soles), only for presentation.</summary>
    public decimal Amount => AmountInCents / 100m;

    public static Money Soles(decimal amount) => new((long)decimal.Round(amount * 100m, 0), DefaultCurrency);

    public override string ToString() => $"{Currency} {Amount:0.00}";
}
