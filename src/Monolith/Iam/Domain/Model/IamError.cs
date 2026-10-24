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

    /// <summary>The password does not meet the password policy.</summary>
    WeakPassword,

    /// <summary>Only parents can create their own account; administrators and drivers are created for them.</summary>
    SelfRegistrationNotAllowed,

    /// <summary>The organization of the account is suspended.</summary>
    OrganizationNotActive,

    OperationCancelled,
    DatabaseError,
    InternalServerError
}
