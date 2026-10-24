namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     When a route operates: departure and estimated arrival (local time of <see cref="TimeZoneId" />) and the
///     days of the week. Two schedules overlap when they share a day and their time windows intersect; that
///     is what makes a driver or vehicle unavailable for another route (US-05 S2).
/// </summary>
/// <remarks>
///     The operating days are stored as a bit mask (bit <c>n</c> = <see cref="DayOfWeek" /> <c>n</c>) so the
///     value object maps to a single column.
/// </remarks>
public record RouteSchedule
{
    public const string DefaultTimeZoneId = "America/Lima";

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    public RouteSchedule()
    {
        DepartureTime = TimeOnly.MinValue;
        EstimatedArrivalTime = TimeOnly.MinValue;
        TimeZoneId = DefaultTimeZoneId;
        OperatingDaysMask = 0;
    }

    public RouteSchedule(TimeOnly departureTime, TimeOnly estimatedArrivalTime, string timeZoneId,
        IEnumerable<DayOfWeek> operatingDays)
    {
        ArgumentNullException.ThrowIfNull(operatingDays);
        if (estimatedArrivalTime <= departureTime)
            throw new ArgumentException("The estimated arrival time must be later than the departure time.",
                nameof(estimatedArrivalTime));

        var zone = (timeZoneId ?? string.Empty).Trim();
        if (!IsKnownTimeZone(zone))
            throw new ArgumentException($"Unknown time zone '{timeZoneId}'.", nameof(timeZoneId));

        var mask = 0;
        foreach (var day in operatingDays)
        {
            if (!Enum.IsDefined(day))
                throw new ArgumentException($"'{day}' is not a valid day of the week.", nameof(operatingDays));
            mask |= 1 << (int)day;
        }

        if (mask == 0)
            throw new ArgumentException("A route must operate at least one day of the week.", nameof(operatingDays));

        DepartureTime = departureTime;
        EstimatedArrivalTime = estimatedArrivalTime;
        TimeZoneId = zone;
        OperatingDaysMask = mask;
    }

    public TimeOnly DepartureTime { get; init; }

    public TimeOnly EstimatedArrivalTime { get; init; }

    public string TimeZoneId { get; init; }

    public int OperatingDaysMask { get; init; }

    /// <summary>Operating days ordered from Sunday to Saturday.</summary>
    public IReadOnlyList<DayOfWeek> OperatingDays => Enum.GetValues<DayOfWeek>().Where(OperatesOn).ToList();

    public TimeSpan Duration => EstimatedArrivalTime - DepartureTime;

    public bool OperatesOn(DayOfWeek day) => (OperatingDaysMask & (1 << (int)day)) != 0;

    /// <summary>
    ///     True when both schedules share at least one day and their [departure, arrival) windows intersect.
    ///     Back-to-back routes (one arrives 07:30, the next departs 07:30) do not overlap.
    /// </summary>
    public bool OverlapsWith(RouteSchedule other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return (OperatingDaysMask & other.OperatingDaysMask) != 0 &&
               DepartureTime < other.EstimatedArrivalTime &&
               other.DepartureTime < EstimatedArrivalTime;
    }

    /// <summary>Calendar date of <paramref name="instant" /> in the time zone of the route.</summary>
    public DateOnly LocalDateOf(DateTimeOffset instant)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    private static bool IsKnownTimeZone(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
