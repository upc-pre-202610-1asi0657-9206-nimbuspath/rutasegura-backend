using Monolith.Shared.Application.Model;
using Monolith.Stakeholder.Domain.Model;
using Monolith.Stakeholder.Domain.Model.Exceptions;

namespace Monolith.Stakeholder.Application.Internal.CommandServices;

/// <summary>Shared plumbing of the Stakeholder application services.</summary>
internal static class StakeholderOperation
{
    /// <summary>Runs a use case and turns broken business rules into <see cref="Result{T}" /> failures.</summary>
    public static async Task<Result<T>> ExecuteAsync<T>(Func<Task<Result<T>>> useCase)
    {
        try
        {
            return await useCase();
        }
        catch (StakeholderDomainException ex)
        {
            return Result<T>.Failure(ex.Error, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<T>.Failure(StakeholderError.InvalidStakeholderData, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return Result<T>.Failure(StakeholderError.OperationCancelled, "The operation was cancelled.");
        }
    }

    /// <summary>Today's date in Peru (birth dates are validated against it).</summary>
    public static DateOnly Today(TimeProvider clock)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }
}
