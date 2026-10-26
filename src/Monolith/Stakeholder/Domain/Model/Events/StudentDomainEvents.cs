using Monolith.Shared.Domain.Model.Events;
using Monolith.Stakeholder.Domain.Model.ValueObjects;

namespace Monolith.Stakeholder.Domain.Model.Events;

// Domain events of the Student aggregate. The application layer turns the ones other services care about into
// integration events (stakeholder.guardian-student-linked / -unlinked, consumed by Trip & Tracking).

public abstract record StakeholderDomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}

/// <summary>A parent registered a child: the parent may follow the trips of this student.</summary>
public sealed record StudentRegisteredEvent(StudentId StudentId, Guid GuardianUserId, DateTimeOffset OccurredAt)
    : StakeholderDomainEvent;

/// <summary>The child was removed by its parent: the link guardian → student ends.</summary>
public sealed record StudentRemovedEvent(StudentId StudentId, Guid GuardianUserId, DateTimeOffset OccurredAt)
    : StakeholderDomainEvent;

public sealed record StudentEnrolledEvent(StudentId StudentId, EnrollmentId EnrollmentId, Guid RouteId,
    Guid OrganizationId, DateTimeOffset OccurredAt) : StakeholderDomainEvent;

public sealed record StudentEnrollmentEndedEvent(StudentId StudentId, EnrollmentId EnrollmentId, Guid RouteId,
    Guid OrganizationId, string Status, DateTimeOffset OccurredAt) : StakeholderDomainEvent;

public sealed record StudentPickupLocationChangedEvent(StudentId StudentId, DateTimeOffset OccurredAt)
    : StakeholderDomainEvent;
