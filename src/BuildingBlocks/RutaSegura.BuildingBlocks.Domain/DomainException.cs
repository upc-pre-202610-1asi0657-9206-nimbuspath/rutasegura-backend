namespace RutaSegura.BuildingBlocks.Domain;

/// <summary>
/// Violación de una regla de negocio. <see cref="Code"/> es estable y viaja al cliente
/// en ProblemDetails.extensions["code"] (ej. TRIP_HAS_PENDING_STUDENTS).
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
