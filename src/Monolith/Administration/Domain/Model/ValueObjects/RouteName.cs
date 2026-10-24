namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>Display name of a route, e.g. "Ruta 3 - Colegio San Agustín (mañana)".</summary>
public record RouteName
{
    public const int MinLength = 3;
    public const int MaxLength = 120;

    public RouteName() : this(string.Empty, false)
    {
    }

    public RouteName(string value) : this(value, true)
    {
    }

    public RouteName(string value, bool validate)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (validate && (trimmed.Length < MinLength || trimmed.Length > MaxLength))
            throw new ArgumentException($"Route name must have between {MinLength} and {MaxLength} characters.",
                nameof(value));
        Value = trimmed;
    }

    public string Value { get; init; } = string.Empty;

    public override string ToString() => Value;
}
