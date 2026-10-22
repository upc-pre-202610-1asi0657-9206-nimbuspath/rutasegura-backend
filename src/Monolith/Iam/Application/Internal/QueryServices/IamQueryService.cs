using Monolith.Iam.Application.QueryServices;
using Monolith.Iam.Domain.Model.Aggregates;
using Monolith.Iam.Domain.Model.Queries;
using Monolith.Iam.Domain.Repositories;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Iam.Application.Internal.QueryServices;

public class IamQueryService(IUserRepository userRepository, IOrganizationRepository organizationRepository)
    : IIamQueryService
{
    public Task<User?> Handle(GetUserByIdQuery query, CancellationToken cancellationToken) =>
        userRepository.FindByUserIdAsync(new UserId(query.UserId), cancellationToken);

    public async Task<IEnumerable<User>> Handle(GetAllUsersQuery query, CancellationToken cancellationToken) =>
        await userRepository.ListAsync(cancellationToken);

    public Task<Organization?> Handle(GetOrganizationByIdQuery query, CancellationToken cancellationToken) =>
        organizationRepository.FindByOrganizationIdAsync(new OrganizationId(query.OrganizationId),
            cancellationToken);
}