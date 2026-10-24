using Monolith.Administration.Application.Internal.OutboundServices.ACL;
using Monolith.Iam.Interfaces.ACL;

namespace Monolith.Administration.Infrastructure.Acl;

/// <summary>
///     Adapter of the <see cref="IIamAccountService" /> port over the facade Iam publishes
///     (<see cref="IIamContextFacade" />). Administration never reads Iam's aggregates or tables.
/// </summary>
public class IamAccountService(IIamContextFacade iam) : IIamAccountService
{
    public async Task<bool> IsDriverAccountOfOrganizationAsync(Guid userId, Guid organizationId,
        CancellationToken cancellationToken)
    {
        var account = await iam.FetchUserAccountAsync(userId, cancellationToken);
        return account is not null &&
               account.HasRole(IamRoles.Driver) &&
               account.OrganizationId == organizationId;
    }
}
