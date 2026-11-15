using Monolith.Shared.Domain.Repositories;
using Monolith.Stakeholder.Domain.Model.Aggregates;

namespace Monolith.Stakeholder.Domain.Repositories;

public interface IGuardianProfileRepository : IBaseRepository<GuardianProfile>
{
    Task<GuardianProfile?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GuardianProfile>> FindByUserIdsAsync(IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
}
