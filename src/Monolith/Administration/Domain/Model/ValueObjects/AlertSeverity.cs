namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>How urgently the operation must look at an alert. An SOS is always CRITICAL.</summary>
public record AlertSeverity
{
    public const string Low = "LOW";
    public const string Medium = "MEDIUM";
    public const string High = "HIGH";
    public const string Critical = "CRITICAL";

    private static readonly string[] AllowedValues = [Low, Medium, High, Critical];

    public AlertSeverity() : this(Medium, false)
    {
    }

    public AlertSeverity(string value) : this(value, true)
    {
    }

    public AlertSeverity(string value, bool validate)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (validate && !AllowedValues.Contains(normalized))
            throw new ArgumentException($"'{value}' is not a valid alert severity.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = Medium;

    public static AlertSeverity CreateCritical() => new(Critical);

    public bool IsCritical() => Value == Critical;

    public override string ToString() => Value;
}
