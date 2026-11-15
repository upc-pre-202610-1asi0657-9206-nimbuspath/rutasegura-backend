namespace Monolith.Iam.Interfaces.ACL;

/// <summary>Role names Iam issues in the JWT (<c>RoleTier</c>), published for other bounded contexts.</summary>
public static class IamRoles
{
    public const string Admin = "ADMIN";
    public const string Driver = "DRIVER";
    public const string Parent = "PARENT";
}
