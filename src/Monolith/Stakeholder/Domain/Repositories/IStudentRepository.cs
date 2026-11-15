using Monolith.Shared.Domain.Repositories;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Model.ValueObjects;

namespace Monolith.Stakeholder.Domain.Repositories;

public interface IStudentRepository : IBaseRepository<Student>
{
    /// <summary>Loads the student with its enrollments.</summary>
    Task<Student?> FindByStudentIdAsync(StudentId studentId, CancellationToken cancellationToken);

    /// <summary>Children of a guardian that were not removed.</summary>
    Task<IReadOnlyList<Student>> FindActiveByGuardianAsync(Guid guardianUserId, CancellationToken cancellationToken);

    Task<int> CountActiveByGuardianAsync(Guid guardianUserId, CancellationToken cancellationToken);

    /// <summary>The student that owns the enrollment.</summary>
    Task<Student?> FindByEnrollmentIdAsync(EnrollmentId enrollmentId, CancellationToken cancellationToken);

    /// <summary>Students with at least one enrollment (any status) in a route of the organization.</summary>
    Task<IReadOnlyList<Student>> FindEnrolledInOrganizationAsync(Guid organizationId, Guid? routeId,
        CancellationToken cancellationToken);

    /// <summary>Students with an ACTIVE enrollment in any of the routes.</summary>
    Task<IReadOnlyList<Student>> FindActivelyEnrolledInAsync(IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken);
}
