using Microsoft.AspNetCore.Authorization;
using Monolith.Iam.Interfaces.ACL;
using Monolith.Iam.Interfaces.Security;

namespace Monolith.Iam.Infrastructure.Authorization;

/// <summary>
///     Checks the token claims against Iam (through its own facade): the user exists, its role and organization are the
///     ones in the token, and the organization is ACTIVE. Any mismatch denies access (403).
/// </summary>
public sealed class ActiveIamAccountHandler(IIamContextFacade iam, ILogger<ActiveIamAccountHandler> logger)
    : AuthorizationHandler<ActiveIamAccountRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        ActiveIamAccountRequirement requirement)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated != true) return;

        var userId = principal.GetUserId();
        var role = principal.GetRole();
        var organizationId = principal.GetOrganizationId();
        if (userId is null || string.IsNullOrWhiteSpace(role)) return;
        if (requirement.RequiredRole is not null &&
            !string.Equals(role, requirement.RequiredRole, StringComparison.OrdinalIgnoreCase)) return;
        if (requirement.RequiresOrganization && organizationId is null) return;

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        var account = await iam.FetchUserAccountAsync(userId.Value, cancellationToken);
        if (account is null || !account.HasRole(role) || account.OrganizationId != organizationId)
        {
            logger.LogWarning("Token of user {UserId} no longer matches its Iam account: access denied", userId);
            return;
        }

        if (account.OrganizationId is { } organization &&
            !await iam.IsOrganizationActiveAsync(organization, cancellationToken))
        {
            logger.LogWarning("Organization {OrganizationId} is not active: access denied to {UserId}", organization,
                userId);
            return;
        }

        context.Succeed(requirement);
    }
}
