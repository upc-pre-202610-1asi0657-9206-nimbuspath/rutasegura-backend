namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>Follow-up of an alert by the operation: OPEN → ACKNOWLEDGED → RESOLVED.</summary>
public record AlertStatus
{
    public const string Open = "OPEN";
    public const string Acknowledged = "ACKNOWLEDGED";
    public const string Resolved = "RESOLVED";

    private static readonly string[] AllowedValues = [Open, Acknowledged, Resolved];

    public AlertStatus() : this(Open, false)
    {
    }

    public AlertStatus(string value) : this(value, true)
    {
    }

    public AlertStatus(string value, bool validate)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (validate && !AllowedValues.Contains(normalized))
            throw new ArgumentException($"'{value}' is not a valid alert status.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = Open;

    public static AlertStatus CreateOpen() => new(Open);

    public static AlertStatus CreateAcknowledged() => new(Acknowledged);

    public static AlertStatus CreateResolved() => new(Resolved);

    public bool IsOpen() => Value == Open;

    public bool IsAcknowledged() => Value == Acknowledged;

    public bool IsResolved() => Value == Resolved;

    public override string ToString() => Value;
}
