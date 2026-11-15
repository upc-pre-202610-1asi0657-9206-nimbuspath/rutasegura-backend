using Monolith.Iam.Domain.Model.Aggregates;
using Monolith.Iam.Domain.Model.Commands;
using Monolith.Shared.Application.Model;

namespace Monolith.Iam.Application.CommandServices;

public interface IIamCommandService
{
    /// <summary>Self-registration of a PARENT account.</summary>
    Task<Result<User>> Handle(SignUpCommand command, CancellationToken cancellationToken);

    Task<Result<(User User, string Token)>> Handle(SignInCommand command, CancellationToken cancellationToken);

    /// <summary>Organization + its single administrator (after a confirmed subscription).</summary>
    Task<Result<(Organization Organization, User Administrator)>> Handle(ProvisionOrganizationCommand command,
        CancellationToken cancellationToken);

    /// <summary>DRIVER account created by the administrator of the organization.</summary>
    Task<Result<User>> Handle(RegisterDriverAccountCommand command, CancellationToken cancellationToken);

    Task<Result<Organization>> Handle(UpdateOrganizationCommand command, CancellationToken cancellationToken);
}
