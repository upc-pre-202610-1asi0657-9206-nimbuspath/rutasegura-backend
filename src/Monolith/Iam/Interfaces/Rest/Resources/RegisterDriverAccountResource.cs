namespace Monolith.Iam.Interfaces.Rest.Resources;

/// <summary>Account of a driver, created by the administrator of the organization in the token.</summary>
public record RegisterDriverAccountResource(string FirstName, string LastName, string Email, string Password);
