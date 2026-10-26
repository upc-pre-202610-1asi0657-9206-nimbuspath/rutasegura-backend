using Microsoft.EntityFrameworkCore;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Shared.Infrastructure.Persistence.EFC.Repositories;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Repositories;

namespace Monolith.Stakeholder.Infrastructure.Persistence.EFC.Repositories;

public class GuardianProfileRepository(AppDbContext context)
    : BaseRepository<GuardianProfile>(context), IGuardianProfileRepository
{
    public Task<GuardianProfile?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Context.Set<GuardianProfile>().FirstOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<GuardianProfile>> FindByUserIdsAsync(IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0) return [];
        return await Context.Set<GuardianProfile>().WhereIn(profile => profile.UserId, userIds)
            .ToListAsync(cancellationToken);
    }
}
