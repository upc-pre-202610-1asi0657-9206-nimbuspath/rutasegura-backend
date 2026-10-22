using Monolith.Iam.Domain.Model.Aggregates;
using Monolith.Iam.Domain.Model.Queries;

namespace Monolith.Iam.Application.QueryServices;

public interface IIamQueryService
{
    Task<User?> Handle(GetUserByIdQuery query, CancellationToken cancellationToken);

    Task<IEnumerable<User>> Handle(GetAllUsersQuery query, CancellationToken cancellationToken);

    Task<Organization?> Handle(GetOrganizationByIdQuery query, CancellationToken cancellationToken);
}