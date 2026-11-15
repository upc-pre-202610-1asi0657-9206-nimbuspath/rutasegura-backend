namespace Monolith.Iam.Interfaces.Rest.Resources;

/// <summary>
///     Self-registration from the app: always a PARENT account. <c>RoleTier</c> is accepted for compatibility with
///     existing clients but any value other than PARENT is rejected (403).
/// </summary>
public record SignUpResource(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? RoleTier,
    Guid? OrganizationId);
