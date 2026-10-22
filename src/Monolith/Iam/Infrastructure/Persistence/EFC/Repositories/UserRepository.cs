using Microsoft.EntityFrameworkCore;
using Monolith.Iam.Domain.Model.Aggregates;
using Monolith.Iam.Domain.Model.ValueObjects;
using Monolith.Iam.Domain.Repositories;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Shared.Infrastructure.Persistence.EFC.Repositories;

namespace Monolith.Iam.Infrastructure.Persistence.EFC.Repositories;

public class UserRepository(AppDbContext context) : BaseRepository<User>(context), IUserRepository
{
    public Task<User?> FindByUserIdAsync(UserId userId, CancellationToken cancellationToken) =>
        Context.Set<User>().FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = new Email(email, false);
        return Context.Set<User>().FirstOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);
    }

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = new Email(email, false);
        return Context.Set<User>().AnyAsync(user => user.Email == normalizedEmail, cancellationToken);
    }
}