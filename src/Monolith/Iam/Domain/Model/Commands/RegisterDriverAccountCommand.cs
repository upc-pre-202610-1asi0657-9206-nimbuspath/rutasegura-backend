namespace Monolith.Iam.Domain.Model.Commands;

/// <summary>The administrator of an organization creates the account of one of its drivers.</summary>
public record RegisterDriverAccountCommand(
    Guid OrganizationId,
    string FirstName,
    string LastName,
    string Email,
    string Password);
