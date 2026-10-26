namespace Monolith.Iam.Interfaces.ACL;

/// <summary>
///     Published facade of the Iam bounded context (Anti-Corruption Layer). Other contexts of the monolith
///     (Administration, Subscription, …) ask Iam about accounts only through this contract and never touch its
///     aggregates, repositories or tables.
/// </summary>
public interface IIamContextFacade
{
    /// <summary>Account data other contexts may rely on, or null when the user does not exist.</summary>
    Task<IamUserAccount?> FetchUserAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Display name of the organization (transport company), or null when it does not exist.</summary>
    Task<string?> FetchOrganizationNameAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>True when the organization exists and is ACTIVE (not suspended).</summary>
    Task<bool> IsOrganizationActiveAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    ///     Creates an organization with its single administrator. Called by Subscription once the payment of a plan is
    ///     confirmed (until then, by the platform through <c>POST /api/v1/organizations</c> with the provisioning key).
    /// </summary>
    Task<IamProvisioningResult> ProvisionOrganizationAsync(string organizationName, string administratorFirstName,
        string administratorLastName, string administratorEmail, string administratorPassword,
        CancellationToken cancellationToken);
}

/// <summary>Primitive snapshot of an Iam user (Published Language: no Iam domain types).</summary>
/// <param name="UserId">Iam user id (the <c>NameIdentifier</c> claim of the JWT).</param>
/// <param name="Role">ADMIN | DRIVER | PARENT.</param>
/// <param name="OrganizationId">Organization (tenant) of the account, if any.</param>
public sealed record IamUserAccount(Guid UserId, string Role, Guid? OrganizationId, string FullName, string Email)
{
    public bool HasRole(string role) => string.Equals(Role, role, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Outcome of provisioning; <c>Error</c> is the name of the Iam error when it failed.</summary>
public sealed record IamProvisioningResult(
    bool Succeeded,
    Guid? OrganizationId,
    Guid? AdministratorUserId,
    string? Error,
    string? Message);
