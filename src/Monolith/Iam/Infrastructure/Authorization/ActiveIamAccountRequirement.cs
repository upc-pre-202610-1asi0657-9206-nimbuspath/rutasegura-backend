using Microsoft.AspNetCore.Authorization;

namespace Monolith.Iam.Infrastructure.Authorization;

/// <summary>
///     The JWT is not enough: the account must still be valid in Iam. A token is valid for days, so without this
///     check a suspended organization, a deleted account or a changed role would keep its access until it expires.
/// </summary>
/// <param name="RequiredRole">Role the account must have (null: any role).</param>
/// <param name="RequiresOrganization">The token must carry an <c>organizationId</c> (tenant-scoped endpoints).</param>
public sealed record ActiveIamAccountRequirement(string? RequiredRole, bool RequiresOrganization)
    : IAuthorizationRequirement;
