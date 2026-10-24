namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     Lifecycle of a route: DRAFT → ACTIVE ⇄ INACTIVE. Only ACTIVE routes are operated by Trip &amp; Tracking
///     and only they count against the active-route limit of the contracted plan.
/// </summary>
public record RouteStatus
{
    public const string Draft = "DRAFT";
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";

    private static readonly string[] AllowedValues = [Draft, Active, Inactive];

    public RouteStatus() : this(Draft, false)
    {
    }

    public RouteStatus(string value) : this(value, true)
    {
    }

    public RouteStatus(string value, bool validate)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (validate && !AllowedValues.Contains(normalized))
            throw new ArgumentException($"'{value}' is not a valid route status.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = Draft;

    public static RouteStatus CreateDraft() => new(Draft);

    public static RouteStatus CreateActive() => new(Active);

    public static RouteStatus CreateInactive() => new(Inactive);

    public bool IsDraft() => Value == Draft;

    public bool IsActive() => Value == Active;

    public bool IsInactive() => Value == Inactive;

    public override string ToString() => Value;
}
