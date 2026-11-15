using Microsoft.AspNetCore.Mvc;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.TripTracking.Api.Errors;
using RutaSegura.TripTracking.Api.Resources;
using RutaSegura.TripTracking.Api.Security;
using RutaSegura.TripTracking.Application.Common;

namespace RutaSegura.TripTracking.Api.Controllers;

/// <summary>
/// Humble Object (5.1.2): los controllers solo traducen HTTP ⇄ comandos/queries y delegan en <see cref="IDispatcher"/> (R-02).
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class TripTrackingControllerBase(IDispatcher dispatcher, TimeProvider clock) : ControllerBase
{
    /// <summary>Las fechas "de hoy" se calculan en la zona del negocio (Lima, UTC-5).</summary>
    protected const string BusinessTimeZone = "America/Lima";

    protected IDispatcher Dispatcher => dispatcher;

    protected Guid CurrentUserId => User.UserId();

    protected Requester CurrentRequester => User.ToRequester();

    protected async Task<IActionResult> Send<T>(ICommand<T> command, CancellationToken ct, Func<T, IActionResult>? onSuccess = null)
    {
        if (CurrentUserId == Guid.Empty) return Unauthorized();
        var result = await dispatcher.SendAsync(command, ct);
        return result.IsSuccess ? (onSuccess?.Invoke(result.Value) ?? Ok(result.Value)) : this.ToProblem(result.Errors);
    }

    protected async Task<IActionResult> Query<T>(IQuery<T> query, CancellationToken ct)
    {
        if (CurrentUserId == Guid.Empty) return Unauthorized();
        var result = await dispatcher.QueryAsync(query, ct);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Errors);
    }

    protected IActionResult Invalid(string code, string message) =>
        this.ToProblem([Error.Validation(code, message)]);

    protected static PositionInput? ToInput(PositionResource? position) =>
        position is null ? null : new PositionInput(position.Latitude, position.Longitude);

    /// <summary>[from, to) por defecto: el día de hoy en Lima.</summary>
    protected (DateTimeOffset From, DateTimeOffset To) RangeOrToday(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is { } f && to is { } t) return (f, t);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZone);
        var localNow = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        var start = new DateTimeOffset(localNow.Date, localNow.Offset);
        var begin = from ?? start;
        return (begin, to ?? begin.AddDays(1));
    }
}
