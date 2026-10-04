using RutaSegura.BuildingBlocks.Application.Results;

namespace RutaSegura.BuildingBlocks.Application.Validation;

/// <summary>Validación de forma del mensaje (campos requeridos, tamaños). Las reglas de negocio viven en el dominio.</summary>
public interface IValidator<in T>
{
    IEnumerable<Error> Validate(T instance);
}
