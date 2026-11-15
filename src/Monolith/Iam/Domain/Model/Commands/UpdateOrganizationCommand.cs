namespace Monolith.Iam.Domain.Model.Commands;

public record UpdateOrganizationCommand(Guid OrganizationId, string Name);