namespace Monolith.Iam.Domain.Model.Queries;

/// <summary>Accounts of one organization (an administrator never sees accounts of other organizations).</summary>
public record GetUsersByOrganizationQuery(Guid OrganizationId);
