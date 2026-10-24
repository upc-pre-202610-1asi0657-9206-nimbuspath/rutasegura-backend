using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Application.Model;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Administration.Application.Internal.CommandServices;

/// <summary>Shared plumbing of the Administration command services.</summary>
internal static class AdministrationOperation
{
    /// <summary>
    ///     Runs a use case and turns domain rule violations into <see cref="Result{T}" /> failures, so the
    ///     interface layer receives an <see cref="AdministrationError" /> instead of an exception.
    /// </summary>
    public static async Task<Result<T>> ExecuteAsync<T>(Func<Task<Result<T>>> useCase)
    {
        try
        {
            return await useCase();
        }
        catch (AdministrationDomainException ex)
        {
            return Result<T>.Failure(ex.Error, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<T>.Failure(AdministrationError.InvalidAdministrationData, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return Result<T>.Failure(AdministrationError.OperationCancelled, "The operation was cancelled.");
        }
    }

    /// <summary>Organization of the authenticated administrator (taken from the JWT, never from the body).</summary>
    public static OrganizationId Organization(Guid organizationId) =>
        organizationId == Guid.Empty
            ? throw new ArgumentException("The organization of the authenticated user is required.",
                nameof(organizationId))
            : new OrganizationId(organizationId);

    /// <summary>Today's date for the operation (school routes run in Peru).</summary>
    public static DateOnly Today(TimeProvider clock)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(RouteSchedule.DefaultTimeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }

    public static GeoLocation? TryLocation(double? latitude, double? longitude)
    {
        if (latitude is null || longitude is null) return null;
        try
        {
            return new GeoLocation(latitude.Value, longitude.Value);
        }
        catch (ArgumentException)
        {
            // An SOS or incident is never rejected because of an invalid GPS fix.
            return null;
        }
    }
}
