using Monolith.Iam.Interfaces.ACL;
using Monolith.Stakeholder.Application.Internal.OutboundServices.ACL;

namespace Monolith.Stakeholder.Infrastructure.Acl;

/// <summary>Anti-Corruption Layer towards Iam (its published facade only).</summary>
public class GuardianDirectory(IIamContextFacade iam) : IGuardianDirectory
{
    public async Task<GuardianAccount?> FindAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await iam.FetchUserAccountAsync(userId, cancellationToken);
        return account is null ? null : new GuardianAccount(account.UserId, account.FullName, account.Email, account.Role);
    }

    public Task<string?> FindOrganizationNameAsync(Guid organizationId, CancellationToken cancellationToken) =>
        iam.FetchOrganizationNameAsync(organizationId, cancellationToken);

    public Task<bool> IsOrganizationActiveAsync(Guid organizationId, CancellationToken cancellationToken) =>
        iam.IsOrganizationActiveAsync(organizationId, cancellationToken);
}
