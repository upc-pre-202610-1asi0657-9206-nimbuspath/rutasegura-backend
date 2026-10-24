namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     Direction of a school route. On a PICKUP route the stops are where each child is picked up and the
///     trip ends at the school; on a RETURN route the trip starts at the school and the stops are where each
///     child is dropped off.
/// </summary>
public record RouteDirection
{
    public const string Pickup = "PICKUP";
    public const string Return = "RETURN";

    private static readonly string[] AllowedValues = [Pickup, Return];

    public RouteDirection() : this(Pickup, false)
    {
    }

    public RouteDirection(string value) : this(value, true)
    {
    }

    public RouteDirection(string value, bool validate)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (validate && !AllowedValues.Contains(normalized))
            throw new ArgumentException($"'{value}' is not a valid route direction (PICKUP or RETURN).", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = Pickup;

    public static RouteDirection CreatePickup() => new(Pickup);

    public static RouteDirection CreateReturn() => new(Return);

    public bool IsPickup() => Value == Pickup;

    public bool IsReturn() => Value == Return;

    /// <summary>What happens to the students of a stop on this route.</summary>
    public string StopPurpose => IsPickup() ? "PICK_UP" : "DROP_OFF";

    public override string ToString() => Value;
}
