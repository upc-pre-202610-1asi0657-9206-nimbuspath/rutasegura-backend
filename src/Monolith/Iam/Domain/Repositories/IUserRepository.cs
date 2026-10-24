using Monolith.Iam.Domain.Model.Aggregates;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Repositories;

namespace Monolith.Iam.Domain.Repositories;

public interface IUserRepository : IBaseRepository<User>
{
    Task<User?> FindByUserIdAsync(UserId userId, CancellationToken cancellationToken);

    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken);

    Task<IEnumerable<User>> FindAllByOrganizationIdAsync(OrganizationId organizationId,
        CancellationToken cancellationToken);
}
