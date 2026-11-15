using System.Text.RegularExpressions;

namespace Monolith.Iam.Domain.Model.ValueObjects;

/// <summary>
///     Value object representing a user's email address, used as their sign-in identifier.
/// </summary>
public record Email
{
    public const int MaxLength = 255;

    public Email() : this(string.Empty, false)
    {
    }

    public Email(string value) : this(value, true)
    {
    }

    public Email(string value, bool validate)
    {
        if (validate && !IsValidEmail(value))
            throw new ArgumentException("Invalid email address.", nameof(value));
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; init; } = string.Empty;

    public override string ToString() => Value;

    private static bool IsValidEmail(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaxLength &&
        Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
}