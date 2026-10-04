using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Application.Validation;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Application.Trips.Commands;

// ───────────────────────── Reportar incidente ─────────────────────────

public sealed record ReportIncidentCommand(
    Guid TripId, Guid DriverId, Guid ClientEventId, IncidentType Type, string? Detail, bool NotifyGuardians,
    DateTimeOffset? OccurredAt = null, PositionInput? Position = null) : ICommand<DriverActionResult>;

internal sealed class ReportIncidentCommandValidator : IValidator<ReportIncidentCommand>
{
    public IEnumerable<Error> Validate(ReportIncidentCommand command)
    {
        if (!Enum.IsDefined(command.Type))
            yield return Error.Validation("VALIDATION_INCIDENT_TYPE", "Tipo de incidente desconocido.");
    }
}

internal sealed class ReportIncidentCommandHandler(ITripRepository trips, TimeProvider clock)
    : ICommandHandler<ReportIncidentCommand, DriverActionResult>
{
    public async Task<Result<DriverActionResult>> HandleAsync(ReportIncidentCommand command, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadForDriverAsync(command.TripId, command.DriverId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;

        var applied = trip.ReportIncident(new DriverId(command.DriverId), command.Type, command.Detail, command.NotifyGuardians,
            new ClientEventId(command.ClientEventId), command.OccurredAt, command.Position?.ToDomain(), clock.GetUtcNow());
        return DriverActionResult.From(trip, applied);
    }
}

// ───────────────────────── SOS ─────────────────────────

public sealed record RaiseSosCommand(
    Guid TripId, Guid DriverId, Guid ClientEventId, DateTimeOffset? OccurredAt = null, PositionInput? Position = null)
    : ICommand<DriverActionResult>;

internal sealed class RaiseSosCommandHandler(ITripRepository trips, TimeProvider clock)
    : ICommandHandler<RaiseSosCommand, DriverActionResult>
{
    public async Task<Result<DriverActionResult>> HandleAsync(RaiseSosCommand command, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadForDriverAsync(command.TripId, command.DriverId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;

        // Si la coordenada viene corrupta, igual se dispara el SOS con la última posición conocida.
        var position = command.Position is { } p && IsValid(p) ? p.ToDomain() : null;

        var applied = trip.RaiseSos(new DriverId(command.DriverId), new ClientEventId(command.ClientEventId),
            command.OccurredAt, position, clock.GetUtcNow());
        return DriverActionResult.From(trip, applied);
    }

    private static bool IsValid(PositionInput p) =>
        p.Latitude is >= -90 and <= 90 && p.Longitude is >= -180 and <= 180 && !(p.Latitude == 0 && p.Longitude == 0);
}

public sealed record ResolveSosCommand(Guid TripId, Guid OperatorId) : ICommand<Unit>;

internal sealed class ResolveSosCommandHandler(ITripRepository trips, TimeProvider clock)
    : ICommandHandler<ResolveSosCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ResolveSosCommand command, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadAsync(command.TripId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;

        loaded.Value.ResolveSos(command.OperatorId, clock.GetUtcNow());
        return Unit.Value;
    }
}
