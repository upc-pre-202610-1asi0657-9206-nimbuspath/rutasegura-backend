using Monolith.Shared.Application.Model;
using Monolith.Stakeholder.Application.Internal.OutboundServices.ACL;
using Monolith.Stakeholder.Application.QueryServices;
using Monolith.Stakeholder.Domain.Model;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Model.Queries;
using Monolith.Stakeholder.Domain.Model.ValueObjects;
using Monolith.Stakeholder.Domain.Repositories;

namespace Monolith.Stakeholder.Application.Internal.QueryServices;

/// <summary>
///     Read side of Stakeholder. Route names, statuses and the stop of each child come from Route Planning at read
///     time (the administrator may rename a route or move a child to another stop).
/// </summary>
public class StakeholderQueryService(
    IStudentRepository studentRepository,
    IGuardianProfileRepository guardianProfileRepository,
    IStudentPhotoStore photoStore,
    IRoutePlanningService routePlanning,
    IGuardianDirectory directory) : IStakeholderQueryService
{
    private const string Admin = "ADMIN";
    private const string Driver = "DRIVER";
    private const string Parent = "PARENT";

    public async Task<IReadOnlyList<StudentDetails>> Handle(GetMyStudentsQuery query,
        CancellationToken cancellationToken)
    {
        if (query.GuardianUserId == Guid.Empty) return [];
        var students = await studentRepository.FindActiveByGuardianAsync(query.GuardianUserId, cancellationToken);
        return await ToDetailsAsync(students, cancellationToken);
    }

    public async Task<StudentDetails?> Handle(GetMyStudentQuery query, CancellationToken cancellationToken)
    {
        if (query.GuardianUserId == Guid.Empty || query.StudentId == Guid.Empty) return null;
        var student = await studentRepository.FindByStudentIdAsync(new StudentId(query.StudentId), cancellationToken);
        if (student is null || !student.IsGuardedBy(query.GuardianUserId) || student.IsRemoved()) return null;
        return (await ToDetailsAsync([student], cancellationToken)).Single();
    }

    public async Task<GuardianProfileDetails> Handle(GetGuardianProfileQuery query,
        CancellationToken cancellationToken)
    {
        var account = await directory.FindAccountAsync(query.GuardianUserId, cancellationToken);
        var profile = await guardianProfileRepository.FindByUserIdAsync(query.GuardianUserId, cancellationToken);
        return new GuardianProfileDetails(query.GuardianUserId, account, profile);
    }

    public async Task<RouteOffer?> Handle(GetRouteOfferQuery query, CancellationToken cancellationToken)
    {
        var route = await routePlanning.FindRouteByEnrollmentCodeAsync(query.EnrollmentCode ?? "", cancellationToken);
        if (route is null) return null;
        return new RouteOffer(route, await directory.FindOrganizationNameAsync(route.OrganizationId, cancellationToken));
    }

    public async Task<IReadOnlyList<RouteStudentDetails>> Handle(GetOrganizationEnrollmentsQuery query,
        CancellationToken cancellationToken)
    {
        if (query.OrganizationId == Guid.Empty) return [];
        var students = await studentRepository.FindEnrolledInOrganizationAsync(query.OrganizationId, query.RouteId,
            cancellationToken);
        var rows = students.SelectMany(student => student.Enrollments
                .Where(e => e.OrganizationId == query.OrganizationId &&
                            (query.RouteId is null || e.RouteId == query.RouteId) &&
                            (!query.OnlyActive || e.IsActive()))
                .Select(enrollment => (student, enrollment)))
            .ToList();
        return await ToRouteStudentsAsync(rows, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<RouteStudentDetails>>> Handle(GetRouteStudentsQuery query,
        CancellationToken cancellationToken)
    {
        if (query.OrganizationId == Guid.Empty || query.RouteId == Guid.Empty) return RouteNotFound();

        if (string.Equals(query.RequesterRole, Driver, StringComparison.OrdinalIgnoreCase))
        {
            var operated = await routePlanning.FindRouteIdsOperatedByAsync(query.OrganizationId,
                query.RequesterUserId, cancellationToken);
            if (!operated.Contains(query.RouteId))
                return Result<IReadOnlyList<RouteStudentDetails>>.Failure(StakeholderError.RouteNotOperatedByDriver,
                    "The route is not assigned to the authenticated driver.");
        }
        else
        {
            var route = (await routePlanning.FindRoutesAsync([query.RouteId], cancellationToken)).SingleOrDefault();
            if (route is null || route.OrganizationId != query.OrganizationId) return RouteNotFound();
        }

        return Result<IReadOnlyList<RouteStudentDetails>>.Success(
            await ActiveStudentsOfRoutesAsync([query.RouteId], cancellationToken));
    }

    public async Task<IReadOnlyList<RouteStudentDetails>> Handle(GetDriverStudentsQuery query,
        CancellationToken cancellationToken)
    {
        var routeIds = await routePlanning.FindRouteIdsOperatedByAsync(query.OrganizationId, query.DriverUserId,
            cancellationToken);
        return routeIds.Count == 0 ? [] : await ActiveStudentsOfRoutesAsync(routeIds, cancellationToken);
    }

    /// <summary>
    ///     Minors' photos are never public (AC-01): the guardian, the administrator of a company the child rides with,
    ///     and the driver of one of those routes may see it.
    /// </summary>
    public async Task<Result<StudentPhotoContent>> Handle(GetStudentPhotoQuery query,
        CancellationToken cancellationToken)
    {
        if (query.StudentId == Guid.Empty) return PhotoFailure(StakeholderError.StudentNotFound, "Student was not found.");
        var student = await studentRepository.FindByStudentIdAsync(new StudentId(query.StudentId), cancellationToken);
        if (student is null || student.IsRemoved())
            return PhotoFailure(StakeholderError.StudentNotFound, "Student was not found.");
        if (!await MaySeeStudentAsync(student, query, cancellationToken))
            return PhotoFailure(StakeholderError.StudentAccessDenied, "The account may not see this student.");
        if (student.Photo is null)
            return PhotoFailure(StakeholderError.StudentPhotoNotFound, "The student has no photo.");

        var stored = await photoStore.FindAsync(student.Id, cancellationToken);
        return stored is null
            ? PhotoFailure(StakeholderError.StudentPhotoNotFound, "The student has no photo.")
            : Result<StudentPhotoContent>.Success(new StudentPhotoContent(stored.Value.ContentType,
                stored.Value.Content, student.Photo.Version));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<bool> MaySeeStudentAsync(Student student, GetStudentPhotoQuery query,
        CancellationToken cancellationToken)
    {
        if (string.Equals(query.RequesterRole, Parent, StringComparison.OrdinalIgnoreCase))
            return student.IsGuardedBy(query.RequesterUserId);

        if (query.OrganizationId is not { } organization) return false;
        var routes = student.ActiveEnrollments.Where(e => e.OrganizationId == organization)
            .Select(e => e.RouteId).ToHashSet();
        if (routes.Count == 0) return false;

        if (string.Equals(query.RequesterRole, Admin, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(query.RequesterRole, Driver, StringComparison.OrdinalIgnoreCase)) return false;
        var operated = await routePlanning.FindRouteIdsOperatedByAsync(organization, query.RequesterUserId,
            cancellationToken);
        return operated.Any(routes.Contains);
    }

    private async Task<IReadOnlyList<RouteStudentDetails>> ActiveStudentsOfRoutesAsync(
        IReadOnlyCollection<Guid> routeIds, CancellationToken cancellationToken)
    {
        var students = await studentRepository.FindActivelyEnrolledInAsync(routeIds, cancellationToken);
        var rows = students.SelectMany(student => student.ActiveEnrollments
                .Where(e => routeIds.Contains(e.RouteId))
                .Select(enrollment => (student, enrollment)))
            .ToList();
        return await ToRouteStudentsAsync(rows, cancellationToken);
    }

    private async Task<IReadOnlyList<RouteStudentDetails>> ToRouteStudentsAsync(
        IReadOnlyList<(Student Student, Domain.Model.Entities.RouteEnrollment Enrollment)> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];
        var stops = await routePlanning.FindStudentStopsAsync(
            rows.Select(r => r.Enrollment.RouteId).Distinct().ToList(), cancellationToken);
        var guardianIds = rows.Select(r => r.Student.GuardianUserId).Distinct().ToList();
        var profiles = (await guardianProfileRepository.FindByUserIdsAsync(guardianIds, cancellationToken))
            .ToDictionary(p => p.UserId);
        var accounts = new Dictionary<Guid, GuardianAccount?>();
        foreach (var guardianId in guardianIds)
            accounts[guardianId] = await directory.FindAccountAsync(guardianId, cancellationToken);

        return rows
            .Select(r => new RouteStudentDetails(r.Student, r.Enrollment,
                stops.FirstOrDefault(s => s.RouteId == r.Enrollment.RouteId &&
                                          s.StudentId == r.Student.Id.Identifier),
                accounts.GetValueOrDefault(r.Student.GuardianUserId),
                profiles.GetValueOrDefault(r.Student.GuardianUserId)))
            .OrderBy(d => d.Enrollment.RouteId)
            .ThenBy(d => d.Stop?.Sequence ?? int.MaxValue)
            .ThenBy(d => d.Student.FullName.LastName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<StudentDetails>> ToDetailsAsync(IReadOnlyList<Student> students,
        CancellationToken cancellationToken)
    {
        var routeIds = students.SelectMany(s => s.Enrollments).Select(e => e.RouteId).Distinct().ToList();
        var routes = routeIds.Count == 0
            ? []
            : (await routePlanning.FindRoutesAsync(routeIds, cancellationToken)).ToDictionary(r => r.RouteId);
        var activeRouteIds = students.SelectMany(s => s.ActiveEnrollments).Select(e => e.RouteId).Distinct().ToList();
        var stops = activeRouteIds.Count == 0
            ? []
            : await routePlanning.FindStudentStopsAsync(activeRouteIds, cancellationToken);
        var organizationNames = new Dictionary<Guid, string?>();
        foreach (var organizationId in students.SelectMany(s => s.Enrollments).Select(e => e.OrganizationId).Distinct())
            organizationNames[organizationId] = await directory.FindOrganizationNameAsync(organizationId,
                cancellationToken);

        return students.Select(student => new StudentDetails(student, student.Enrollments
                .Select(enrollment => new EnrollmentDetails(enrollment,
                    routes.GetValueOrDefault(enrollment.RouteId),
                    enrollment.IsActive()
                        ? stops.FirstOrDefault(s => s.RouteId == enrollment.RouteId &&
                                                    s.StudentId == student.Id.Identifier)
                        : null,
                    organizationNames.GetValueOrDefault(enrollment.OrganizationId)))
                .ToList()))
            .ToList();
    }

    private static Result<IReadOnlyList<RouteStudentDetails>> RouteNotFound() =>
        Result<IReadOnlyList<RouteStudentDetails>>.Failure(StakeholderError.RouteNotFound,
            "Route was not found.");

    private static Result<StudentPhotoContent> PhotoFailure(StakeholderError error, string message) =>
        Result<StudentPhotoContent>.Failure(error, message);
}
