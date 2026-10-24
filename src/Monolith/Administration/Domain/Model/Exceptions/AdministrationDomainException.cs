namespace Monolith.Administration.Domain.Model.Exceptions;

/// <summary>
///     Thrown by the Administration domain model when a business rule is violated.
/// </summary>
/// <remarks>
///     Value objects keep using <see cref="ArgumentException" /> for malformed data (as in the Iam context);
///     this exception is reserved for rules that need a specific <see cref="AdministrationError" /> so the
///     interface layer can answer with the right status (for example 409 for a schedule conflict).
/// </remarks>
public sealed class AdministrationDomainException(AdministrationError error, string message) : Exception(message)
{
    public AdministrationError Error { get; } = error;
}
