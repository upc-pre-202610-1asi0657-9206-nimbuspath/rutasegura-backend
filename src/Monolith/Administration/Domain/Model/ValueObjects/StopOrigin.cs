namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     Who created a stop. MANUAL stops are planned by the administrator and stay until the administrator removes them.
///     ENROLLMENT stops are created automatically at a child's pickup address when a parent enrolls the child, and are
///     removed again when their last student leaves the route. The DESTINATION stop is the school: the end of a
///     PICKUP route and the start of a RETURN route.
/// </summary>
public record StopOrigin
{
    public const string Manual = "MANUAL";
    public const string Enrollment = "ENROLLMENT";

    public const string Destination = "DESTINATION";

    private static readonly string[] AllowedValues = [Manual, Enrollment, Destination];

    public StopOrigin() : this(Manual, false)
    {
    }

    public StopOrigin(string value) : this(value, true)
    {
    }

    public StopOrigin(string value, bool validate)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (validate && !AllowedValues.Contains(normalized))
            throw new ArgumentException($"'{value}' is not a valid stop origin.", nameof(value));
        Value = string.IsNullOrEmpty(normalized) ? Manual : normalized;
    }

    public string Value { get; init; } = Manual;

    public static StopOrigin CreateManual() => new(Manual);

    public static StopOrigin CreateEnrollment() => new(Enrollment);

    public static StopOrigin CreateDestination() => new(Destination);

    public bool IsEnrollment() => Value == Enrollment;

    public bool IsDestination() => Value == Destination;

    public override string ToString() => Value;
}
