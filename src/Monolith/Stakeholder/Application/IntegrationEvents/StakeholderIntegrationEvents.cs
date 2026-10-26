using Monolith.Shared.Domain.Model.Events;
using Monolith.Stakeholder.Domain.Model.Events;

namespace Monolith.Stakeholder.Application.IntegrationEvents;

/// <summary>Integration event of the Stakeholder context (exchange <c>stakeholder.events</c>, routing key = type).</summary>
public sealed record StakeholderIntegrationEvent(
    Guid EventId,
    string MessageType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    object Payload);

/// <summary>Message types published by Stakeholder (Published Language).</summary>
public static class StakeholderMessageTypes
{
    /// <summary>Trip &amp; Tracking: the parent may follow the trips of the student (GuardianAccessPolicy).</summary>
    public const string GuardianStudentLinked = "stakeholder.guardian-student-linked";

    public const string GuardianStudentUnlinked = "stakeholder.guardian-student-unlinked";

    /// <summary>For Notification (welcome / goodbye messages); nobody consumes them yet.</summary>
    public const string StudentEnrolled = "stakeholder.student-enrolled";

    public const string StudentEnrollmentEnded = "stakeholder.student-enrollment-ended";
}

/// <summary>Same contract Trip &amp; Tracking already consumes (<c>GuardianStudentLinkV1</c>).</summary>
public sealed record GuardianStudentLinkV1(Guid GuardianId, Guid StudentId);

/// <param name="Status">ACTIVE when enrolled; WITHDRAWN or CANCELLED when it ended.</param>
public sealed record StudentEnrollmentV1(
    Guid EnrollmentId,
    Guid StudentId,
    Guid GuardianId,
    Guid RouteId,
    Guid OrganizationId,
    string Status);

/// <summary>Translates the domain events of a Student into integration events.</summary>
public static class StakeholderIntegrationEventMapper
{
    public const int SchemaVersion = 1;

    public static IReadOnlyList<StakeholderIntegrationEvent> Map(Guid guardianUserId,
        IEnumerable<IDomainEvent> domainEvents)
    {
        var result = new List<StakeholderIntegrationEvent>();
        foreach (var domainEvent in domainEvents)
            switch (domainEvent)
            {
                case StudentRegisteredEvent registered:
                    result.Add(new StakeholderIntegrationEvent(registered.EventId,
                        StakeholderMessageTypes.GuardianStudentLinked, SchemaVersion, registered.OccurredAt,
                        new GuardianStudentLinkV1(registered.GuardianUserId, registered.StudentId.Identifier)));
                    break;
                case StudentRemovedEvent removed:
                    result.Add(new StakeholderIntegrationEvent(removed.EventId,
                        StakeholderMessageTypes.GuardianStudentUnlinked, SchemaVersion, removed.OccurredAt,
                        new GuardianStudentLinkV1(removed.GuardianUserId, removed.StudentId.Identifier)));
                    break;
                case StudentEnrolledEvent enrolled:
                    result.Add(new StakeholderIntegrationEvent(enrolled.EventId,
                        StakeholderMessageTypes.StudentEnrolled, SchemaVersion, enrolled.OccurredAt,
                        new StudentEnrollmentV1(enrolled.EnrollmentId.Identifier, enrolled.StudentId.Identifier,
                            guardianUserId, enrolled.RouteId, enrolled.OrganizationId, "ACTIVE")));
                    break;
                case StudentEnrollmentEndedEvent ended:
                    result.Add(new StakeholderIntegrationEvent(ended.EventId,
                        StakeholderMessageTypes.StudentEnrollmentEnded, SchemaVersion, ended.OccurredAt,
                        new StudentEnrollmentV1(ended.EnrollmentId.Identifier, ended.StudentId.Identifier,
                            guardianUserId, ended.RouteId, ended.OrganizationId, ended.Status)));
                    break;
            }

        return result;
    }
}
