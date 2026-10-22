namespace Monolith.Iam.Interfaces.Rest.Resources;

public record SignUpResource(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string RoleTier,
    Guid? OrganizationId);