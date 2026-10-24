namespace Monolith.Iam.Domain.Model.Commands;

/// <summary>
///     Self-registration from the app. Only PARENT accounts can be created this way; the organization is optional
///     (a parent may register before being linked to a transport operation).
/// </summary>
public record SignUpCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    Guid? OrganizationId);
