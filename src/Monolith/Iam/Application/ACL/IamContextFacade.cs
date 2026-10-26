using Monolith.Iam.Application.CommandServices;
using Monolith.Iam.Domain.Model.Commands;
using Monolith.Iam.Domain.Repositories;
using Monolith.Iam.Interfaces.ACL;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Iam.Application.ACL;

/// <summary>Implementation of the Iam facade on top of the Iam repositories and command service.</summary>
public class IamContextFacade(
    IUserRepository userRepository,
    IOrganizationRepository organizationRepository,
    IIamCommandService commandService) : IIamContextFacade
{
    public async Task<IamUserAccount?> FetchUserAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty) return null;
        var user = await userRepository.FindByUserIdAsync(new UserId(userId), cancellationToken);
        return user is null
            ? null
            : new IamUserAccount(user.Id.Identifier, user.Role.Value, user.OrganizationId?.Identifier,
                user.GetFullName(), user.GetEmail());
    }

    public async Task<string?> FetchOrganizationNameAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty) return null;
        var organization = await organizationRepository.FindByOrganizationIdAsync(new OrganizationId(organizationId),
            cancellationToken);
        return organization?.GetName();
    }

    public async Task<bool> IsOrganizationActiveAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty) return false;
        var organization = await organizationRepository.FindByOrganizationIdAsync(new OrganizationId(organizationId),
            cancellationToken);
        return organization is not null && organization.IsActive();
    }

    public async Task<IamProvisioningResult> ProvisionOrganizationAsync(string organizationName,
        string administratorFirstName, string administratorLastName, string administratorEmail,
        string administratorPassword, CancellationToken cancellationToken)
    {
        var result = await commandService.Handle(new ProvisionOrganizationCommand(organizationName,
            administratorFirstName, administratorLastName, administratorEmail, administratorPassword), cancellationToken);
        return result.IsSuccess
            ? new IamProvisioningResult(true, result.Value.Organization.Id.Identifier,
                result.Value.Administrator.Id.Identifier, null, null)
            : new IamProvisioningResult(false, null, null, result.Error?.ToString(), result.Message);
    }
}
