namespace Monolith.Iam.Domain.Model.Commands;

public record SignUpCommand(
    string FirstName, 
    string LastName, 
    string Email, 
    string Password, 
    string RoleTier, 
    Guid? OrganizationId);