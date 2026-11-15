using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Administration.Domain.Model.Aggregates;

/// <summary>
///     History of a trip completed on one of the operation's routes (<c>trip-tracking.trip-completed</c>).
///     It feeds administrative reports (attendance, punctuality, incidents per route) without querying the
///     Trip &amp; Tracking database.
/// </summary>
public class TripRecord
{
    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected TripRecord()
    {
        Id = new TripRecordId();
        OrganizationId = new OrganizationId();
        TripId = new TripId();
        RouteId = new RouteId();
        RouteName = string.Empty;
    }

    public TripRecord(OrganizationId organizationId, TripId tripId, RouteId routeId, string? routeName,
        int deliveredStudents, int absentStudents, int incidentCount, DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        if (organizationId.Identifier == Guid.Empty)
            throw new ArgumentException("The trip record must belong to an organization.", nameof(organizationId));
        if (deliveredStudents < 0 || absentStudents < 0 || incidentCount < 0)
            throw new ArgumentException("Trip counters cannot be negative.");
        if (completedAt < startedAt)
            throw new ArgumentException("A trip cannot complete before it started.", nameof(completedAt));

        Id = TripRecordId.New();
        OrganizationId = organizationId;
        TripId = tripId ?? throw new ArgumentNullException(nameof(tripId));
        RouteId = routeId ?? throw new ArgumentNullException(nameof(routeId));
        RouteName = string.IsNullOrWhiteSpace(routeName) ? "(unknown route)" : routeName.Trim();
        DeliveredStudents = deliveredStudents;
        AbsentStudents = absentStudents;
        IncidentCount = incidentCount;
        StartedAt = startedAt;
        CompletedAt = completedAt;
    }

    public TripRecordId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public TripId TripId { get; private set; }

    public RouteId RouteId { get; private set; }

    public string RouteName { get; private set; }

    public int DeliveredStudents { get; private set; }

    public int AbsentStudents { get; private set; }

    public int IncidentCount { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    public TimeSpan Duration => CompletedAt - StartedAt;

    /// <summary>Share of planned students that travelled (1 when nobody was expected).</summary>
    public double AttendanceRate
    {
        get
        {
            var expected = DeliveredStudents + AbsentStudents;
            return expected == 0 ? 1d : (double)DeliveredStudents / expected;
        }
    }
}
