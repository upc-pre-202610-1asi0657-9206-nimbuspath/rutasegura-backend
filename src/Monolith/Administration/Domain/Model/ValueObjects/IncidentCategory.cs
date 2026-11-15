namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     Incident types reported by the driver in Trip &amp; Tracking (<c>IncidentType</c>), translated into the
///     Administration vocabulary. Unknown values are accepted as OTHER (tolerant reader), so a new incident
///     type in Trip &amp; Tracking does not break this context.
/// </summary>
public record IncidentCategory
{
    public const string HeavyTraffic = "HEAVY_TRAFFIC";
    public const string RouteDeviation = "ROUTE_DEVIATION";
    public const string VehicleFailure = "VEHICLE_FAILURE";
    public const string StudentUnwell = "STUDENT_UNWELL";
    public const string Accident = "ACCIDENT";
    public const string Other = "OTHER";

    private static readonly Dictionary<string, string> FromTripTracking = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HeavyTraffic"] = HeavyTraffic,
        ["RouteDeviation"] = RouteDeviation,
        ["VehicleFailure"] = VehicleFailure,
        ["StudentUnwell"] = StudentUnwell,
        ["Accident"] = Accident,
        ["Other"] = Other
    };

    private static readonly string[] AllowedValues =
        [HeavyTraffic, RouteDeviation, VehicleFailure, StudentUnwell, Accident, Other];

    public IncidentCategory() : this(Other, false)
    {
    }

    public IncidentCategory(string value) : this(value, true)
    {
    }

    public IncidentCategory(string value, bool validate)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (validate && !AllowedValues.Contains(normalized))
            throw new ArgumentException($"'{value}' is not a valid incident category.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = Other;

    /// <summary>Translates the incident type published by Trip &amp; Tracking ("VehicleFailure", …).</summary>
    public static IncidentCategory FromTripTrackingType(string? type) =>
        type is not null && FromTripTracking.TryGetValue(type.Trim(), out var value)
            ? new IncidentCategory(value)
            : new IncidentCategory(Other);

    /// <summary>Severity the operation gives to each incident category.</summary>
    public AlertSeverity Severity => Value switch
    {
        Accident => new AlertSeverity(AlertSeverity.Critical),
        VehicleFailure or StudentUnwell => new AlertSeverity(AlertSeverity.High),
        RouteDeviation or Other => new AlertSeverity(AlertSeverity.Medium),
        _ => new AlertSeverity(AlertSeverity.Low)
    };

    public override string ToString() => Value;
}
