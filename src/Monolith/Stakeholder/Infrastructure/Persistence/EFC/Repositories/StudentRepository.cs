using Microsoft.EntityFrameworkCore;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Shared.Infrastructure.Persistence.EFC.Repositories;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Model.Entities;
using Monolith.Stakeholder.Domain.Model.ValueObjects;
using Monolith.Stakeholder.Domain.Repositories;

namespace Monolith.Stakeholder.Infrastructure.Persistence.EFC.Repositories;

/// <remarks>Enrollments are auto-included: a Student is always loaded as a whole aggregate.</remarks>
public class StudentRepository(AppDbContext context) : BaseRepository<Student>(context), IStudentRepository
{
    public Task<Student?> FindByStudentIdAsync(StudentId studentId, CancellationToken cancellationToken) =>
        Context.Set<Student>().FirstOrDefaultAsync(student => student.Id == studentId, cancellationToken);

    public async Task<IReadOnlyList<Student>> FindActiveByGuardianAsync(Guid guardianUserId,
        CancellationToken cancellationToken)
    {
        var students = await Context.Set<Student>()
            .Where(student => student.GuardianUserId == guardianUserId && student.Status == Student.Active)
            .ToListAsync(cancellationToken);
        return students.OrderBy(s => s.FullName.FirstName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<int> CountActiveByGuardianAsync(Guid guardianUserId, CancellationToken cancellationToken) =>
        Context.Set<Student>()
            .CountAsync(student => student.GuardianUserId == guardianUserId && student.Status == Student.Active,
                cancellationToken);

    public async Task<Student?> FindByEnrollmentIdAsync(EnrollmentId enrollmentId, CancellationToken cancellationToken)
    {
        var studentId = await Context.Set<RouteEnrollment>()
            .Where(enrollment => enrollment.Id == enrollmentId)
            .Select(enrollment => EF.Property<StudentId>(enrollment, "StudentId"))
            .FirstOrDefaultAsync(cancellationToken);
        return studentId is null ? null : await FindByStudentIdAsync(studentId, cancellationToken);
    }

    public async Task<IReadOnlyList<Student>> FindEnrolledInOrganizationAsync(Guid organizationId, Guid? routeId,
        CancellationToken cancellationToken)
    {
        var enrollments = Context.Set<RouteEnrollment>().Where(e => e.OrganizationId == organizationId);
        if (routeId is { } route) enrollments = enrollments.Where(e => e.RouteId == route);
        return await LoadAsync(enrollments, cancellationToken);
    }

    public async Task<IReadOnlyList<Student>> FindActivelyEnrolledInAsync(IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken)
    {
        if (routeIds.Count == 0) return [];
        var active = EnrollmentStatus.CreateActive();
        return await LoadAsync(
            Context.Set<RouteEnrollment>().WhereIn(e => e.RouteId, routeIds).Where(e => e.Status == active),
            cancellationToken);
    }

    private async Task<IReadOnlyList<Student>> LoadAsync(IQueryable<RouteEnrollment> enrollments,
        CancellationToken cancellationToken)
    {
        var studentIds = await enrollments.Select(e => EF.Property<StudentId>(e, "StudentId")).Distinct()
            .ToListAsync(cancellationToken);
        if (studentIds.Count == 0) return [];
        return await Context.Set<Student>().WhereIn(student => student.Id, studentIds)
            .ToListAsync(cancellationToken);
    }
}
