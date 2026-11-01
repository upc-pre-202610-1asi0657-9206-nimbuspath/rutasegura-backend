namespace Monolith.Subscription.Domain.Model.ValueObjects;

/// <summary>
///     Stable code of a plan of the catalog (e.g. <c>BASIC</c>, <c>COMPLETE</c>). It is the same code Route Planning
///     shows next to its active-route quota, so both contexts speak about the same plan.
/// </summary>
public record PlanCode
{
    public const int MaxLength = 20;

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    public PlanCode()
    {
        Value = string.Empty;
    }

    public PlanCode(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0)
            throw new ArgumentException("The plan code is required.", nameof(value));
        if (normalized.Length > MaxLength || !normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            throw new ArgumentException(
                $"The plan code must have up to {MaxLength} letters, digits, '_' or '-'.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; }

    public override string ToString() => Value;
}
