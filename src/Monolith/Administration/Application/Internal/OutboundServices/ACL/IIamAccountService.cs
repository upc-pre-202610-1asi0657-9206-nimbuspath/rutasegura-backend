namespace Monolith.Administration.Application.Internal.OutboundServices.ACL;

/// <summary>
///     Anti-corruption port towards the Iam bounded context of the monolith. Administration only needs to know
///     whether an account can be linked to a driver profile; it never touches Iam's aggregates.
/// </summary>
public interface IIamAccountService
{
    /// <summary>True when the user exists, has role DRIVER and belongs to the organization.</summary>
    Task<bool> IsDriverAccountOfOrganizationAsync(Guid userId, Guid organizationId,
        CancellationToken cancellationToken);
}
