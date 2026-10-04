using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Common;

public static class ApplicationErrors
{
    public static Error TripNotFound(Guid id) => Error.NotFound("TRIP_NOT_FOUND", $"No existe el viaje {id}.");
    public static Error RouteNotFound(Guid id) => Error.NotFound("ROUTE_NOT_FOUND", $"No existe la ruta {id}.");
    public static Error RouteNotOperating(DateOnly date) =>
        new("ROUTE_NOT_OPERATING", $"La ruta no opera el {date:dddd dd/MM}.", ErrorType.BusinessRule);
    public static readonly Error TripAccessDenied = Error.Forbidden("TRIP_ACCESS_DENIED", "No tienes acceso a este viaje.");
    public static readonly Error DriverMismatch = Error.Forbidden(TripErrors.DriverMismatch, "Este viaje está asignado a otro conductor.");
}

public enum RequesterRole
{
    Driver,
    Guardian,
    Admin
}

/// <summary>Quién pide la información. La API lo construye a partir de los claims del JWT emitido por IAM.</summary>
public sealed record Requester(Guid UserId, RequesterRole Role);

/// <summary>Coordenada tal como llega en el request (aún sin validar).</summary>
public sealed record PositionInput(double Latitude, double Longitude)
{
    public GeoPosition ToDomain() => GeoPosition.Create(Latitude, Longitude);
}

/// <summary>Respuesta común a las acciones del conductor. Applied=false: ya se había registrado (reintento).</summary>
public sealed record DriverActionResult(Guid TripId, bool Applied, string TripStatus)
{
    public static DriverActionResult From(Trip trip, bool applied) => new(trip.Id.Value, applied, trip.Status.ToString());
}

/// <summary>Carga el viaje y verifica que lo opere el conductor que llama (404 / 403 sin depender de excepciones).</summary>
internal static class TripLoader
{
    public static async Task<Result<Trip>> LoadForDriverAsync(
        this ITripRepository trips, Guid tripId, Guid driverId, CancellationToken cancellationToken)
    {
        var trip = await trips.GetByIdAsync(new TripId(tripId), cancellationToken);
        if (trip is null) return ApplicationErrors.TripNotFound(tripId);
        if (!trip.IsOperatedBy(new DriverId(driverId))) return ApplicationErrors.DriverMismatch;
        return trip;
    }

    public static async Task<Result<Trip>> LoadAsync(
        this ITripRepository trips, Guid tripId, CancellationToken cancellationToken)
    {
        var trip = await trips.GetByIdAsync(new TripId(tripId), cancellationToken);
        if (trip is null) return ApplicationErrors.TripNotFound(tripId);
        return trip;
    }
}
