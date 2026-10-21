namespace Monolith.Shared.Application.Model;

/// <summary>
///     Generic Result class for Command Handlers in the Application Layer.
/// </summary>
/// <typeparam name="T">The type of the result value.</typeparam>
public class Result<T>
{
    // Modified constructor to include message and Enum? error
    protected Result(bool isSuccess, T? value, string message, Enum? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Message = message;
        Error = error;
    }


    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; } // Made nullable for failure cases
    public string Message { get; } // New property
    public Enum? Error { get; } // Changed type to Enum?


    // Modified Success method to match new constructor
    public static Result<T> Success(T value)
    {
        return new Result<T>(true, value, string.Empty, null);
    }


    // New Failure method using Enum? and string message
    public static Result<T> Failure(Enum error, string message)
    {
        return new Result<T>(false, default, message, error);
    }


    // Removed old Failure(Error error) and Failure(string code, string message) methods.
    // The previous implementation used a custom Error class.
    // The new strategy replaces that with an Enum and a separate message string.
}


/// <summary>
///     Non-generic Result class for Command Handlers.
/// </summary>
public class Result : Result<object>
{
    // Modified constructor to match the base Result<object> constructor
    private Result(bool isSuccess, string message, Enum? error) : base(isSuccess, null, message, error)
    {
    }


    // Modified Success method to match new constructor
    public static Result Success()
    {
        return new Result(true, string.Empty, null);
    }


    // New Failure method using Enum? and string message
    public new static Result Failure(Enum error, string message)
    {
        return new Result(false, message, error);
    }
    
}