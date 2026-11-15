namespace Monolith.Iam.Domain.Model.Commands;

/// <summary>
///     Creates an organization together with its single administrator. It is the result of a confirmed subscription
///     payment (Subscription context); until that context exists it is exposed to the platform through a provisioning key.
/// </summary>
public record ProvisionOrganizationCommand(
    string OrganizationName,
    string AdministratorFirstName,
    string AdministratorLastName,
    string AdministratorEmail,
    string AdministratorPassword);
