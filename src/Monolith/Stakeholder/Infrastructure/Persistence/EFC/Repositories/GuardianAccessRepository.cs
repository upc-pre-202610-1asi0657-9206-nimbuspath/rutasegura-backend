using Microsoft.EntityFrameworkCore;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Repositories;
namespace Monolith.Stakeholder.Infrastructure.Persistence.EFC.Repositories;
public sealed class GuardianAccessRepository(AppDbContext db) : IGuardianAccessRepository
{
    public Task<GuardianAccessGrant?> FindAsync(Guid organization, Guid guardian, Guid student, CancellationToken ct) => db.Set<GuardianAccessGrant>().SingleOrDefaultAsync(g => g.OrganizationId == organization && g.GuardianUserId == guardian && g.StudentId == student, ct);
    public async Task<IReadOnlyList<GuardianAccessGrant>> ForOrganizationAsync(Guid organization, CancellationToken ct) => await db.Set<GuardianAccessGrant>().Where(g => g.OrganizationId == organization).ToListAsync(ct);
    public async Task<IReadOnlyList<GuardianAccessGrant>> ForGuardianAsync(Guid guardian, CancellationToken ct) => await db.Set<GuardianAccessGrant>().Where(g => g.GuardianUserId == guardian).ToListAsync(ct);
    public void Add(GuardianAccessGrant grant) => db.Add(grant);
    public Task<OrganizationGuardian?> FindGuardianAsync(Guid organization, Guid guardian, CancellationToken ct) => db.Set<OrganizationGuardian>().SingleOrDefaultAsync(g => g.OrganizationId == organization && g.UserId == guardian, ct);
    public async Task<IReadOnlyList<OrganizationGuardian>> GuardiansAsync(Guid organization, CancellationToken ct) => await db.Set<OrganizationGuardian>().Where(g => g.OrganizationId == organization).ToListAsync(ct);
    public void AddGuardian(OrganizationGuardian guardian) => db.Add(guardian);
}
