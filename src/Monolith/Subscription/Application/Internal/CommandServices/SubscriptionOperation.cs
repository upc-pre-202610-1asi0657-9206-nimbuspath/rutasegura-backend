using Monolith.Shared.Application.Model;
using Monolith.Subscription.Domain.Model;
using Monolith.Subscription.Domain.Model.Exceptions;

namespace Monolith.Subscription.Application.Internal.CommandServices;

/// <summary>Shared plumbing of the Subscription application services.</summary>
internal static class SubscriptionOperation
{
    /// <summary>Runs a use case and turns broken business rules into <see cref="Result{T}" /> failures.</summary>
    public static async Task<Result<T>> ExecuteAsync<T>(Func<Task<Result<T>>> useCase)
    {
        try
        {
            return await useCase();
        }
        catch (SubscriptionDomainException ex)
        {
            return Result<T>.Failure(ex.Error, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<T>.Failure(SubscriptionError.InvalidSubscriptionData, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return Result<T>.Failure(SubscriptionError.OperationCancelled, "The operation was cancelled.");
        }
    }

    /// <summary><c>AlreadyApplied</c> → <c>ALREADY_APPLIED</c>: the form the API and the event log use.</summary>
    public static string ToCode(Enum value) =>
        string.Concat(value.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString()))
            .ToUpperInvariant();
}
