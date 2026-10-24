namespace Monolith.Iam.Interfaces.Rest.Resources;

/// <summary>Organization and its single administrator (platform / Subscription).</summary>
public record ProvisionOrganizationResource(string Name, ProvisionAdministratorResource Administrator);

public record ProvisionAdministratorResource(string FirstName, string LastName, string Email, string Password);

public record ProvisionedOrganizationResource(OrganizationResource Organization, UserResource Administrator);
