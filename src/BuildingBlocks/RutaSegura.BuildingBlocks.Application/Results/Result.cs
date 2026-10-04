namespace RutaSegura.BuildingBlocks.Application.Results;

public enum ErrorType
{
    Validation,
    NotFound,
    Forbidden,
    Conflict,
    BusinessRule
}

/// <summary>Error esperado de un caso de uso. La API lo traduce a ProblemDetails (code en extensions).</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
}

public readonly record struct Unit
{
    public static readonly Unit Value = new();
}

/// <summary>Resultado de un caso de uso: éxito con valor o lista de errores (sin excepciones para flujo esperado).</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        Errors = [];
    }

    private Result(IReadOnlyList<Error> errors)
    {
        if (errors.Count == 0) throw new ArgumentException("Un resultado fallido necesita al menos un error.", nameof(errors));
        Errors = errors;
    }

    public bool IsSuccess => Errors.Count == 0;
    public bool IsFailure => !IsSuccess;
    public IReadOnlyList<Error> Errors { get; }
    public Error FirstError => IsFailure ? Errors[0] : throw new InvalidOperationException("El resultado es exitoso.");

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"No hay valor: {FirstError.Code}.");

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new([error]);
    public static Result<T> Failure(IReadOnlyList<Error> errors) => new(errors);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
