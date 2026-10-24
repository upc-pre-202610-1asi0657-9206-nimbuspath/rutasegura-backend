using System.Text.RegularExpressions;

namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     Licence plate of a vehicle. Stored normalized (upper case, without spaces or dashes) so "abc-123"
///     and "ABC123" are the same plate (US-08 S1: the plate cannot be duplicated).
/// </summary>
public record LicensePlate
{
    public LicensePlate() : this(string.Empty, false)
    {
    }

    public LicensePlate(string value) : this(value, true)
    {
    }

    public LicensePlate(string value, bool validate)
    {
        var normalized = (value ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Trim()
            .ToUpperInvariant();
        if (validate && !Regex.IsMatch(normalized, "^[A-Z0-9]{5,7}$"))
            throw new ArgumentException("License plate must have between 5 and 7 letters or digits.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = string.Empty;

    /// <summary>Human readable form, e.g. "ABC-123" for six-character plates.</summary>
    public string Display => Value.Length == 6 ? $"{Value[..3]}-{Value[3..]}" : Value;

    public override string ToString() => Display;
}
