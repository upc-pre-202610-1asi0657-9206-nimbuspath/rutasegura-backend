namespace Monolith.Iam.Domain.Model;

/// <summary>
///     Enumerates the failure cases of the Iam bounded context.
/// </summary>
/// <remarks>
///     Conveyed through the Result pattern from the application layer to the interface layer, where
///     it is mapped to an HTTP status code and an RFC 7807 problem detail.
/// </remarks>
public enum IamError
{
    None,
    UserNotFound,
    EmailAlreadyRegistered,
    InvalidCredentials,
    OrganizationNotFound,
    InvalidIamData,
    OperationCancelled,
    DatabaseError,
    InternalServerError
}