namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>Origin of an operational alert received from Trip &amp; Tracking.</summary>
public record AlertType
{
    public const string Incident = "INCIDENT";
    public const string Sos = "SOS";

    private static readonly string[] AllowedValues = [Incident, Sos];

    public AlertType() : this(Incident, false)
    {
    }

    public AlertType(string value) : this(value, true)
    {
    }

    public AlertType(string value, bool validate)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (validate && !AllowedValues.Contains(normalized))
            throw new ArgumentException($"'{value}' is not a valid alert type.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = Incident;

    public static AlertType CreateIncident() => new(Incident);

    public static AlertType CreateSos() => new(Sos);

    public bool IsIncident() => Value == Incident;

    public bool IsSos() => Value == Sos;

    public override string ToString() => Value;
}
