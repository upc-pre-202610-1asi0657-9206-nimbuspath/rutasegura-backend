using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Application.Validation;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Domain.Common;

namespace RutaSegura.TripTracking.Application.Trips.Commands;

// ───────────────────────── Registrar abordaje ("Subió") ─────────────────────────

public sealed record RecordBoardingCommand(
    Guid TripId, Guid DriverId, Guid StudentId, Guid ClientEventId,
    DateTimeOffset? OccurredAt = null, PositionInput? Position = null) : ICommand<DriverActionResult>;

internal sealed class RecordBoardingCommandHandler(ITripRepository trips, TimeProvider clock)
    : ICommandHandler<RecordBoardingCommand, DriverActionResult>
{
    public async Task<Result<DriverActionResult>> HandleAsync(RecordBoardingCommand command, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadForDriverAsync(command.TripId, command.DriverId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;

        var applied = trip.RecordBoarding(new DriverId(command.DriverId), new StudentId(command.StudentId),
            new ClientEventId(command.ClientEventId), command.OccurredAt, command.Position?.ToDomain(), clock.GetUtcNow());
        return DriverActionResult.From(trip, applied);
    }
}

// ───────────────────────── Registrar bajada ─────────────────────────

public sealed record RecordAlightingCommand(
    Guid TripId, Guid DriverId, Guid StudentId, Guid ClientEventId,
    DateTimeOffset? OccurredAt = null, PositionInput? Position = null) : ICommand<DriverActionResult>;

internal sealed class RecordAlightingCommandHandler(ITripRepository trips, TimeProvider clock)
    : ICommandHandler<RecordAlightingCommand, DriverActionResult>
{
    public async Task<Result<DriverActionResult>> HandleAsync(RecordAlightingCommand command, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadForDriverAsync(command.TripId, command.DriverId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;

        var applied = trip.RecordAlighting(new DriverId(command.DriverId), new StudentId(command.StudentId),
            new ClientEventId(command.ClientEventId), command.OccurredAt, command.Position?.ToDomain(), clock.GetUtcNow());
        return DriverActionResult.From(trip, applied);
    }
}

// ───────────────────────── "No vino" ─────────────────────────

public sealed record MarkStudentAbsentCommand(
    Guid TripId, Guid DriverId, Guid StudentId, Guid ClientEventId, DateTimeOffset? OccurredAt = null)
    : ICommand<DriverActionResult>;

internal sealed class MarkStudentAbsentCommandHandler(ITripRepository trips, TimeProvider clock)
    : ICommandHandler<MarkStudentAbsentCommand, DriverActionResult>
{
    public async Task<Result<DriverActionResult>> HandleAsync(MarkStudentAbsentCommand command, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadForDriverAsync(command.TripId, command.DriverId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;

        var applied = trip.MarkStudentAbsent(new DriverId(command.DriverId), new StudentId(command.StudentId),
            new ClientEventId(command.ClientEventId), command.OccurredAt, clock.GetUtcNow());
        return DriverActionResult.From(trip, applied);
    }
}

// ───────────────────────── "Deshacer" ─────────────────────────

public sealed record UndoPassengerRecordCommand(Guid TripId, Guid DriverId, Guid StudentId, Guid ClientEventId)
    : ICommand<DriverActionResult>;

internal sealed class UndoPassengerRecordCommandHandler(ITripRepository trips, TimeProvider clock)
    : ICommandHandler<UndoPassengerRecordCommand, DriverActionResult>
{
    public async Task<Result<DriverActionResult>> HandleAsync(UndoPassengerRecordCommand command, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadForDriverAsync(command.TripId, command.DriverId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;
        var trip = loaded.Value;

        var applied = trip.UndoPassengerRecord(new DriverId(command.DriverId), new StudentId(command.StudentId),
            new ClientEventId(command.ClientEventId), clock.GetUtcNow());
        return DriverActionResult.From(trip, applied);
    }
}

// ───────────────────────── Aviso de inasistencia del apoderado ─────────────────────────

public sealed record NotifyAbsenceCommand(Guid TripId, Guid GuardianId, Guid StudentId, string? Note) : ICommand<Unit>;

internal sealed class NotifyAbsenceCommandValidator : IValidator<NotifyAbsenceCommand>
{
    public IEnumerable<Error> Validate(NotifyAbsenceCommand command)
    {
        if (command.Note?.Length > 250)
            yield return Error.Validation("VALIDATION_NOTE_TOO_LONG", "La nota admite hasta 250 caracteres.");
    }
}

internal sealed class NotifyAbsenceCommandHandler(ITripRepository trips, IGuardianDirectory guardians, TimeProvider clock)
    : ICommandHandler<NotifyAbsenceCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(NotifyAbsenceCommand command, CancellationToken cancellationToken)
    {
        var loaded = await trips.LoadAsync(command.TripId, cancellationToken);
        if (loaded.IsFailure) return loaded.FirstError;

        // Un tutor solo puede avisar por sus propios hijos (AC-01).
        var children = await guardians.GetStudentsOfGuardianAsync(new GuardianId(command.GuardianId), cancellationToken);
        var studentId = new StudentId(command.StudentId);
        if (!children.Contains(studentId)) return ApplicationErrors.TripAccessDenied;

        loaded.Value.NotifyGuardianAbsence(studentId, command.Note, clock.GetUtcNow());
        return Unit.Value;
    }
}
