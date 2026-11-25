using Monolith.Stakeholder.Application.QueryServices;
using Monolith.Stakeholder.Application.Internal.OutboundServices.ACL;
using Monolith.Stakeholder.Domain.Model.ValueObjects;
using Monolith.Stakeholder.Domain.Repositories;
namespace Monolith.Stakeholder.Application.Internal.QueryServices;
public sealed class GuardianAccessQueryService(IGuardianAccessRepository grants, IStudentRepository students, IGuardianDirectory directory, IRoutePlanningService routes, IGuardianProfileRepository profiles) : IGuardianAccessQueryService
{
    public Task<IReadOnlyList<Domain.Model.Aggregates.GuardianAccessGrant>> ForGuardianAsync(Guid guardian, CancellationToken ct) => grants.ForGuardianAsync(guardian, ct);
    public Task<IReadOnlyList<Domain.Model.Aggregates.Student>> RosterAsync(Guid organization, CancellationToken ct)
        => ReadRosterAsync(organization, null, ct);

    public Task<IReadOnlyList<Domain.Model.Aggregates.Student>> RosterAsync(Guid organization, IReadOnlyCollection<Guid> studentIds, CancellationToken ct)
        => ReadRosterAsync(organization, studentIds.ToHashSet(), ct);

    private async Task<IReadOnlyList<Domain.Model.Aggregates.Student>> ReadRosterAsync(Guid organization, HashSet<Guid>? requested, CancellationToken ct)
    {
        var result = new List<Domain.Model.Aggregates.Student>();
        foreach (var id in (await grants.ForOrganizationAsync(organization, ct)).Select(g => g.StudentId).Distinct())
        {
            if (requested is not null && !requested.Contains(id)) continue;
            var student = await students.FindByStudentIdAsync(new StudentId(id), ct);
            if (student is not null && !student.IsRemoved()) result.Add(student);
        }
        return result;
    }
    public Task<IReadOnlyList<Domain.Model.Aggregates.GuardianAccessGrant>> ListAsync(Guid organization, CancellationToken ct) => grants.ForOrganizationAsync(organization, ct);
    public async Task<IReadOnlyList<OperationGuardianDetails>> GuardiansAsync(Guid organization, CancellationToken ct)
    {
        var registered = await grants.GuardiansAsync(organization, ct);
        var ids = registered.Select(g => g.UserId).Concat((await grants.ForOrganizationAsync(organization, ct)).Select(g => g.GuardianUserId)).Distinct();
        var result = new List<OperationGuardianDetails>();
        foreach (var id in ids)
        {
            var account = await directory.FindAccountAsync(id, ct);
            if (account?.Role != "PARENT") continue;
            var contact = registered.FirstOrDefault(g => g.UserId == id);
            var profile = contact is null ? await profiles.FindByUserIdAsync(id, ct) : null;
            result.Add(new(organization, id, account.FullName, account.Email, contact?.PhoneNumber ?? profile?.PhoneNumber.Value, contact?.DocumentNumber ?? profile?.DocumentNumber));
        }
        return result;
    }
    public async Task<IReadOnlyList<AuthorizedGuardianStudent>> CurrentAsync(Guid? organization, Guid? guardian, IReadOnlyCollection<Guid>? studentIds, Guid? route, CancellationToken ct, bool requireActiveEnrollment = true)
    {
        if (organization is null && guardian is null) return [];
        var candidates = guardian is { } user ? await grants.ForGuardianAsync(user, ct) : await grants.ForOrganizationAsync(organization!.Value, ct);
        var result = new List<AuthorizedGuardianStudent>();
        foreach (var grant in candidates.Where(g => g.Status == "VERIFIED" && (organization is null || g.OrganizationId == organization) && (studentIds is null || studentIds.Contains(g.StudentId))))
        {
            if (!await directory.IsOrganizationActiveAsync(grant.OrganizationId, ct) || (await directory.FindAccountAsync(grant.GuardianUserId, ct))?.Role != "PARENT") continue;
            var student = await students.FindByStudentIdAsync(new StudentId(grant.StudentId), ct);
            if (student is null || student.IsRemoved() || student.GuardianUserId != grant.GuardianUserId ||
                (requireActiveEnrollment && !student.ActiveEnrollments.Any(e => e.OrganizationId == grant.OrganizationId && (route is null || e.RouteId == route)))) continue;
            result.Add(new(grant.OrganizationId, grant.GuardianUserId, grant.StudentId));
        }
        return result.Distinct().ToList();
    }
    public async Task<IReadOnlyList<Guid>> OperatorsAsync(Guid organization, CancellationToken ct) =>
        !await directory.IsOrganizationActiveAsync(organization, ct) ? [] : (await directory.FindOrganizationOperatorsAsync(organization, ct)).ToArray();
    public async Task<bool> OwnsRouteAsync(Guid organization, Guid route, CancellationToken ct) =>
        (await routes.FindRoutesAsync(new[] { route }, ct)).Any(r => r.OrganizationId == organization);
}
