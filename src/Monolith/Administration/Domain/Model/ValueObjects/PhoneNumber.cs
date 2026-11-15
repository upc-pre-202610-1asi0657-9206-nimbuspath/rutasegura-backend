using System.Text.RegularExpressions;

namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>Contact phone number of a driver. Spaces and dashes are ignored.</summary>
public record PhoneNumber
{
    public PhoneNumber() : this(string.Empty, false)
    {
    }

    public PhoneNumber(string value) : this(value, true)
    {
    }

    public PhoneNumber(string value, bool validate)
    {
        var normalized = (value ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Trim();
        if (validate && !Regex.IsMatch(normalized, @"^\+?[0-9]{7,15}$"))
            throw new ArgumentException("Phone number must contain between 7 and 15 digits.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = string.Empty;

    public override string ToString() => Value;
}
