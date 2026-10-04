using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Application.Validation;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Trips.Commands;

// ───────────────────────── Programar viaje del día ─────────────────────────
// Lo invoca el job diario (Etapa 4) o el consumidor del evento RoutePublished de Administration.

public sealed record ScheduleTripCommand(Guid RouteId, DateOnly ServiceDate) : ICommand<Guid>;

internal sealed class ScheduleTripCommandHandler(
    IRouteCatalog routes,
    ITripRepository trips,
    TimeProvider clock) : ICommandHandler<ScheduleTripCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(ScheduleTripCommand command, CancellationToken cancellationToken)
    {
        var route = await routes.GetAsync(new RouteId(command.RouteId), cancellationToken);
        if (route is null) return ApplicationErrors.RouteNotFound(command.RouteId);
        if (!route.OperatesOn(command.ServiceDate)) return ApplicationErrors.RouteNotOperating(command.ServiceDate);

        var scheduledStart = route.ScheduledStartOn(command.ServiceDate);

        // Idempotente: el job puede correr dos veces o el evento llegar duplicado.
        var existing = await trips.FindByRouteAndScheduledStartAsync(new RouteId(route.RouteId), scheduledStart, cancellationToken);
        if (existing is not null) return existing.Id.Value;

        var trip = Trip.Schedule(
            TripId.New(),
            new RouteId(route.RouteId),
            route.Name,
            route.Direction,
            new DriverId(route.DriverId),
            new VehicleId(route.VehicleId),
            scheduledStart,
            route.Stops.Select(s => new TripStopDefinition(
                new StopId(s.StopId), s.Sequence, s.Name, GeoPosition.Create(s.Latitude, s.Longitude))).ToList(),
            route.Students.Select(s => new TripPassengerDefinition(new StudentId(s.StudentId), new StopId(s.StopId))).ToList(),
            clock.GetUtcNow());

        trips.Add(trip);
        return trip.Id.Value;
    }
}

// ───────────────────────── Iniciar viaje ─────────────────────────

public sealed record StartTripCommand(Guid TripId, Guid DriverId, Guid ClientEventId) : ICommand<DriverActionResult>;

internal sealed class StartTripCommandHandler(ITripRepository trips, TimeProvider clock)
    : ICommandHandler<StartTripCommand, DriverActionResult>
{
    public async Task<Result<DriverActionResult>> HandleAsync(StartTripCommand command, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadForDriverAsync(command.TripId, command.DriverId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;

        var applied = trip.Start(new DriverId(command.DriverId), new ClientEventId(command.ClientEventId), clock.GetUtcNow());
        return DriverActionResult.From(trip, applied);
    }
}

// ───────────────────────── Finalizar viaje ─────────────────────────

public sealed record CompleteTripCommand(Guid TripId, Guid DriverId, Guid ClientEventId) : ICommand<DriverActionResult>;

internal sealed class CompleteTripCommandHandler(ITripRepository trips, TimeProvider clock)
    : ICommandHandler<CompleteTripCommand, DriverActionResult>
{
    public async Task<Result<DriverActionResult>> HandleAsync(CompleteTripCommand command, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadForDriverAsync(command.TripId, command.DriverId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;

        var applied = trip.Complete(new DriverId(command.DriverId), new ClientEventId(command.ClientEventId), clock.GetUtcNow());
        return DriverActionResult.From(trip, applied);
    }
}

// ───────────────────────── Cancelar viaje (empresa) ─────────────────────────

public sealed record CancelTripCommand(Guid TripId, string Reason) : ICommand<Unit>;

internal sealed class CancelTripCommandValidator : IValidator<CancelTripCommand>
{
    public IEnumerable<Error> Validate(CancelTripCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            yield return Error.Validation(TripErrors.CancellationReasonRequired, "Indica el motivo de la cancelación.");
        else if (command.Reason.Length > 250)
            yield return Error.Validation("VALIDATION_REASON_TOO_LONG", "El motivo admite hasta 250 caracteres.");
    }
}

internal sealed class CancelTripCommandHandler(ITripRepository trips, TimeProvider clock)
    : ICommandHandler<CancelTripCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CancelTripCommand command, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadAsync(command.TripId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;

        loaded.Value.Cancel(command.Reason, clock.GetUtcNow());
        return Unit.Value;
    }
}
