using Monolith.Iam.Domain.Model.Aggregates;
using Monolith.Iam.Domain.Model.Commands;
using Monolith.Shared.Application.Model;

namespace Monolith.Iam.Application.CommandServices;

public interface IIamCommandService
{
    Task<Result<User>> Handle(SignUpCommand command, CancellationToken cancellationToken);

    Task<Result<(User User, string Token)>> Handle(SignInCommand command, CancellationToken cancellationToken);

    Task<Result<Organization>> Handle(CreateOrganizationCommand command, CancellationToken cancellationToken);

    Task<Result<Organization>> Handle(UpdateOrganizationCommand command, CancellationToken cancellationToken);
}