using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Administration.Domain.Model.Aggregates;

/// <summary>
///     Something that happened during a trip and that the operation must follow up: an incident reported by the
///     driver (<c>trip-tracking.incident-reported</c>) or an SOS (<c>trip-tracking.sos-raised</c>).
/// </summary>
/// <remarks>
///     Trip &amp; Tracking owns the trip; this aggregate is the administrator's follow-up of the alert. An SOS is
///     resolved in Trip &amp; Tracking (it blocks the trip until then), so here it can only be acknowledged and is
///     closed when <c>trip-tracking.sos-resolved</c> arrives. Incidents are closed by the administrator.
/// </remarks>
public partial class OperationalAlert
{
    public const int DetailMaxLength = 500;
    private const string UnknownRouteName = "(unknown route)";

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected OperationalAlert()
    {
        Id = new OperationalAlertId();
        OrganizationId = new OrganizationId();
        Type = AlertType.CreateIncident();
        Severity = new AlertSeverity();
        Status = AlertStatus.CreateOpen();
        TripId = new TripId();
        RouteId = new RouteId();
        RouteName = string.Empty;
    }

    private OperationalAlert(OrganizationId organizationId, AlertType type, AlertSeverity severity, TripId tripId,
        RouteId routeId, string? routeName, GeoLocation? location, int affectedStudents, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        if (organizationId.Identifier == Guid.Empty)
            throw new ArgumentException("The alert must belong to an organization.", nameof(organizationId));
        if (affectedStudents < 0)
            throw new ArgumentException("Affected students cannot be negative.", nameof(affectedStudents));

        Id = OperationalAlertId.New();
        OrganizationId = organizationId;
        Type = type;
        Severity = severity;
        Status = AlertStatus.CreateOpen();
        TripId = tripId ?? throw new ArgumentNullException(nameof(tripId));
        RouteId = routeId ?? throw new ArgumentNullException(nameof(routeId));
        RouteName = string.IsNullOrWhiteSpace(routeName) ? UnknownRouteName : routeName.Trim();
        Location = location;
        AffectedStudents = affectedStudents;
        OccurredAt = occurredAt;
    }

    public OperationalAlertId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AlertType Type { get; private set; }

    public AlertSeverity Severity { get; private set; }

    public AlertStatus Status { get; private set; }

    public TripId TripId { get; private set; }

    public RouteId RouteId { get; private set; }

    /// <summary>Route name at the moment of the alert (the route may be renamed later).</summary>
    public string RouteName { get; private set; }

    /// <summary>Incident identifier in Trip &amp; Tracking; makes incident processing idempotent.</summary>
    public Guid? IncidentId { get; private set; }

    public IncidentCategory? Category { get; private set; }

    public string? Detail { get; private set; }

    /// <summary>Last known position; an SOS never waits for GPS, so it may be missing.</summary>
    public GeoLocation? Location { get; private set; }

    /// <summary>Students affected by an incident, or on board when an SOS was raised.</summary>
    public int AffectedStudents { get; private set; }

    /// <summary>IAM account of the driver who raised the SOS.</summary>
    public Guid? DriverUserId { get; private set; }

    public Guid? VehicleId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public UserId? AcknowledgedBy { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public Guid? ResolvedBy { get; private set; }

    public static OperationalAlert FromIncident(OrganizationId organizationId, TripId tripId, RouteId routeId,
        string? routeName, Guid incidentId, IncidentCategory category, string? detail, GeoLocation? location,
        int affectedStudents, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (incidentId == Guid.Empty)
            throw new ArgumentException("Incident identifier is required.", nameof(incidentId));

        return new OperationalAlert(organizationId, AlertType.CreateIncident(), category.Severity, tripId, routeId,
            routeName, location, affectedStudents, occurredAt)
        {
            IncidentId = incidentId,
            Category = category,
            Detail = Truncate(detail)
        };
    }

    public static OperationalAlert FromSos(OrganizationId organizationId, TripId tripId, RouteId routeId,
        string? routeName, Guid driverUserId, Guid vehicleId, GeoLocation? location, int studentsOnBoard,
        DateTimeOffset occurredAt) =>
        new(organizationId, AlertType.CreateSos(), AlertSeverity.CreateCritical(), tripId, routeId, routeName,
            location, studentsOnBoard, occurredAt)
        {
            DriverUserId = driverUserId == Guid.Empty ? null : driverUserId,
            VehicleId = vehicleId == Guid.Empty ? null : vehicleId
        };

    public bool IsOpen() => !Status.IsResolved();

    /// <summary>An administrator confirms the operation is attending the alert.</summary>
    public void Acknowledge(UserId administrator, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        if (!Status.IsOpen())
            throw new AdministrationDomainException(AdministrationError.AlertInvalidTransition,
                $"Only open alerts can be acknowledged (current status: {Status}).");
        Status = AlertStatus.CreateAcknowledged();
        AcknowledgedAt = at;
        AcknowledgedBy = administrator;
    }

    /// <summary>An administrator closes the follow-up of an incident.</summary>
    public void ResolveByAdministrator(UserId administrator, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        if (Type.IsSos())
            throw new AdministrationDomainException(AdministrationError.AlertInvalidTransition,
                "An SOS is resolved from Trip & Tracking; here it can only be acknowledged.");
        Close(administrator.Identifier, at);
    }

    /// <summary>Trip &amp; Tracking reported that the SOS was attended.</summary>
    public void ResolveFromTripTracking(Guid resolvedBy, DateTimeOffset at)
    {
        if (!Type.IsSos())
            throw new AdministrationDomainException(AdministrationError.AlertInvalidTransition,
                "Only SOS alerts are resolved from Trip & Tracking.");
        Close(resolvedBy, at);
    }

    private void Close(Guid resolvedBy, DateTimeOffset at)
    {
        if (Status.IsResolved())
            throw new AdministrationDomainException(AdministrationError.AlertInvalidTransition,
                "The alert is already resolved.");
        Status = AlertStatus.CreateResolved();
        ResolvedAt = at;
        ResolvedBy = resolvedBy == Guid.Empty ? null : resolvedBy;
    }

    private static string? Truncate(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return null;
        var trimmed = detail.Trim();
        return trimmed.Length <= DetailMaxLength ? trimmed : trimmed[..DetailMaxLength];
    }
}
