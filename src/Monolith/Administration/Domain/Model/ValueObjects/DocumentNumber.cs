using System.Text.RegularExpressions;

namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     Identity document of a driver (DNI with 8 digits or Carné de Extranjería up to 12 characters).
///     It is unique per operation (US-01 S3).
/// </summary>
public record DocumentNumber
{
    public const int MinLength = 8;
    public const int MaxLength = 12;

    public DocumentNumber() : this(string.Empty, false)
    {
    }

    public DocumentNumber(string value) : this(value, true)
    {
    }

    public DocumentNumber(string value, bool validate)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (validate && !Regex.IsMatch(normalized, $"^[A-Z0-9]{{{MinLength},{MaxLength}}}$"))
            throw new ArgumentException("Document number must have between 8 and 12 letters or digits.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = string.Empty;

    public override string ToString() => Value;
}
