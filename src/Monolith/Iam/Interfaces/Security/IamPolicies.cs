namespace Monolith.Iam.Interfaces.Security;

/// <summary>
///     Authorization policies provided by Iam to every bounded context of the monolith. All of them require a valid
///     Iam JWT and an account that is still valid in Iam (exists, same role and organization as in the token, and the
///     organization is active).
/// </summary>
public static class IamPolicies
{
    /// <summary>Any active Iam account.</summary>
    public const string Account = "iam:account";

    /// <summary>Administrator (role ADMIN) of an organization: the token carries <c>organizationId</c>.</summary>
    public const string OrganizationAdministrator = "iam:organization-administrator";

    /// <summary>Driver (role DRIVER) of an organization.</summary>
    public const string OrganizationDriver = "iam:organization-driver";

    /// <summary>Parent / guardian (role PARENT): self-registered, does not belong to an organization.</summary>
    public const string Guardian = "iam:guardian";

    /// <summary>Staff of an organization (role ADMIN or DRIVER); the token carries <c>organizationId</c>.</summary>
    public const string OrganizationStaff = "iam:organization-staff";
}
