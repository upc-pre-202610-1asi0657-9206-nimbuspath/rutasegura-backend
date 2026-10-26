using Monolith.Stakeholder.Domain.Model.Exceptions;
using Monolith.Stakeholder.Domain.Model.ValueObjects;

namespace Monolith.Stakeholder.Domain.Model.Entities;

/// <summary>
///     Enrollment of a child in a route of a transport company, made by the parent with the code the company shared.
///     Only the <see cref="Aggregates.Student" /> aggregate creates or ends it. The route itself (and the stop where the
///     child is picked up) belongs to Route Planning; here we only keep its reference and a few facts for display.
/// </summary>
public class RouteEnrollment
{
    public const int RouteNameMaxLength = 100;
    public const int EndReasonMaxLength = 300;

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected RouteEnrollment()
    {
        Id = new EnrollmentId();
        Status = EnrollmentStatus.CreateActive();
        RouteName = string.Empty;
        Direction = string.Empty;
    }

    internal RouteEnrollment(Guid routeId, Guid organizationId, string routeName, string direction,
        DateTimeOffset enrolledAt)
    {
        if (routeId == Guid.Empty) throw new ArgumentException("The route is required.", nameof(routeId));
        if (organizationId == Guid.Empty)
            throw new ArgumentException("The organization of the route is required.", nameof(organizationId));
        var normalizedDirection = (direction ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedDirection is not ("PICKUP" or "RETURN"))
            throw new ArgumentException("The direction must be PICKUP or RETURN.", nameof(direction));

        Id = EnrollmentId.New();
        RouteId = routeId;
        OrganizationId = organizationId;
        RouteName = Truncate((routeName ?? string.Empty).Trim(), RouteNameMaxLength);
        Direction = normalizedDirection;
        Status = EnrollmentStatus.CreateActive();
        EnrolledAt = enrolledAt;
    }

    public EnrollmentId Id { get; private set; }

    public Guid RouteId { get; private set; }

    /// <summary>Transport company that owns the route (its administrator sees and can cancel the enrollment).</summary>
    public Guid OrganizationId { get; private set; }

    /// <summary>Name of the route when the child was enrolled (display only; Route Planning has the current one).</summary>
    public string RouteName { get; private set; }

    /// <summary>PICKUP (home → school) or RETURN (school → home).</summary>
    public string Direction { get; private set; }

    public EnrollmentStatus Status { get; private set; }

    public DateTimeOffset EnrolledAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public string? EndReason { get; private set; }

    public bool IsActive() => Status.IsActive();

    internal void End(EnrollmentStatus status, DateTimeOffset now, string? reason)
    {
        if (!IsActive())
            throw new StakeholderDomainException(StakeholderError.EnrollmentNotActive,
                $"The enrollment is already {Status}.");
        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Status = status;
        EndedAt = now;
        EndReason = trimmed is null ? null : Truncate(trimmed, EndReasonMaxLength);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
