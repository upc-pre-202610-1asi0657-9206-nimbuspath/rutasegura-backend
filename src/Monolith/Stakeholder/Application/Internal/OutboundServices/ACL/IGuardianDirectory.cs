namespace Monolith.Stakeholder.Application.Internal.OutboundServices.ACL;

/// <summary>Port towards Iam: names of the accounts and state of the transport companies.</summary>
public interface IGuardianDirectory
{
    Task<GuardianAccount?> FindAccountAsync(Guid userId, CancellationToken cancellationToken);

    Task<string?> FindOrganizationNameAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<bool> IsOrganizationActiveAsync(Guid organizationId, CancellationToken cancellationToken);
}

public sealed record GuardianAccount(Guid UserId, string FullName, string Email, string Role);
