namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>Whether a vehicle can be used in new assignments (US-08 S3).</summary>
public record VehicleStatus
{
    public const string Enabled = "ENABLED";
    public const string Disabled = "DISABLED";

    private static readonly string[] AllowedValues = [Enabled, Disabled];

    public VehicleStatus() : this(Enabled, false)
    {
    }

    public VehicleStatus(string value) : this(value, true)
    {
    }

    public VehicleStatus(string value, bool validate)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (validate && !AllowedValues.Contains(normalized))
            throw new ArgumentException($"'{value}' is not a valid vehicle status.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = Enabled;

    public static VehicleStatus CreateEnabled() => new(Enabled);

    public static VehicleStatus CreateDisabled() => new(Disabled);

    public bool IsEnabled() => Value == Enabled;

    public bool IsDisabled() => Value == Disabled;

    public override string ToString() => Value;
}
