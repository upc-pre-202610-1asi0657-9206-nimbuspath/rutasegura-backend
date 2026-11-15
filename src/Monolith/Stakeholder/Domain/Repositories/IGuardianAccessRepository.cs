using Monolith.Stakeholder.Domain.Model.Aggregates;
namespace Monolith.Stakeholder.Domain.Repositories;
public interface IGuardianAccessRepository
{
    Task<GuardianAccessGrant?> FindAsync(Guid organization, Guid guardian, Guid student, CancellationToken ct);
    Task<IReadOnlyList<GuardianAccessGrant>> ForOrganizationAsync(Guid organization, CancellationToken ct);
    Task<IReadOnlyList<GuardianAccessGrant>> ForGuardianAsync(Guid guardian, CancellationToken ct);
    void Add(GuardianAccessGrant grant);
    Task<OrganizationGuardian?> FindGuardianAsync(Guid organization, Guid guardian, CancellationToken ct);
    Task<IReadOnlyList<OrganizationGuardian>> GuardiansAsync(Guid organization, CancellationToken ct);
    void AddGuardian(OrganizationGuardian guardian);
}
