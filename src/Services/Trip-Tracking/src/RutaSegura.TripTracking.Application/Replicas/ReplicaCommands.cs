using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Application.Validation;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Domain.Common;

namespace RutaSegura.TripTracking.Application.Replicas;

// Réplicas locales alimentadas por eventos de otros bounded contexts (Database per Service, PT-02).
// Este servicio nunca consulta la base de Administration ni la de IAM: guarda una copia de lo que necesita.
// Los invocan los consumidores de RabbitMQ (Infrastructure) a través del IDispatcher, así pasan por
// Logging → Validation → Transaction igual que cualquier otro caso de uso.

// ───────────────────────── Rutas (Administration / Route Planning) ─────────────────────────

public sealed record UpsertRouteSnapshotCommand(RouteSnapshot Route) : ICommand<Unit>;

internal sealed class UpsertRouteSnapshotCommandValidator : IValidator<UpsertRouteSnapshotCommand>
{
    public IEnumerable<Error> Validate(UpsertRouteSnapshotCommand command)
    {
        var route = command.Route;
        if (route is null)
        {
            yield return Error.Validation("VALIDATION_ROUTE_REQUIRED", "El mensaje no trae la ruta.");
            yield break;
        }

        if (route.RouteId == Guid.Empty) yield return Error.Validation("VALIDATION_ROUTE_ID", "La ruta necesita un identificador.");
        if (string.IsNullOrWhiteSpace(route.Name)) yield return Error.Validation("VALIDATION_ROUTE_NAME", "La ruta necesita un nombre.");
        if (route.DriverId == Guid.Empty || route.VehicleId == Guid.Empty)
            yield return Error.Validation("VALIDATION_ROUTE_ASSIGNMENT", "La ruta necesita conductor y vehículo asignados.");
        if (route.Stops is null || route.Stops.Count == 0)
            yield return Error.Validation("VALIDATION_ROUTE_STOPS", "La ruta necesita al menos una parada.");
        if (route.OperatingDays is null || route.OperatingDays.Count == 0)
            yield return Error.Validation("VALIDATION_ROUTE_DAYS", "La ruta necesita al menos un día de operación.");
        if (!IsKnownTimeZone(route.TimeZoneId))
            yield return Error.Validation("VALIDATION_ROUTE_TIMEZONE", $"Zona horaria desconocida: {route.TimeZoneId}.");

        if (route.Stops is { Count: > 0 } && route.Students is not null)
        {
            var stopIds = route.Stops.Select(s => s.StopId).ToHashSet();
            if (route.Students.Any(s => !stopIds.Contains(s.StopId)))
                yield return Error.Validation("VALIDATION_ROUTE_STUDENT_STOP", "Todos los alumnos deben estar asignados a una parada de la ruta.");
        }
    }

    private static bool IsKnownTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}

internal sealed class UpsertRouteSnapshotCommandHandler(IRouteCatalogStore routes) : ICommandHandler<UpsertRouteSnapshotCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpsertRouteSnapshotCommand command, CancellationToken cancellationToken)
    {
        // Los viajes ya programados conservan su copia de paradas y alumnos; el cambio aplica a los siguientes.
        await routes.UpsertAsync(command.Route, cancellationToken);
        return Unit.Value;
    }
}

public sealed record RemoveRouteSnapshotCommand(Guid RouteId) : ICommand<Unit>;

internal sealed class RemoveRouteSnapshotCommandHandler(IRouteCatalogStore routes) : ICommandHandler<RemoveRouteSnapshotCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemoveRouteSnapshotCommand command, CancellationToken cancellationToken)
    {
        await routes.RemoveAsync(new RouteId(command.RouteId), cancellationToken);
        return Unit.Value;
    }
}

// ───────────────────────── Vínculos tutor → alumno (Roster / IAM, US-02) ─────────────────────────

public sealed record LinkGuardianStudentCommand(Guid GuardianId, Guid StudentId) : ICommand<Unit>;

public sealed record UnlinkGuardianStudentCommand(Guid GuardianId, Guid StudentId) : ICommand<Unit>;

internal sealed class LinkGuardianStudentCommandValidator : IValidator<LinkGuardianStudentCommand>
{
    public IEnumerable<Error> Validate(LinkGuardianStudentCommand command)
    {
        if (command.GuardianId == Guid.Empty || command.StudentId == Guid.Empty)
            yield return Error.Validation("VALIDATION_GUARDIAN_LINK", "El vínculo necesita tutor y alumno.");
    }
}

internal sealed class LinkGuardianStudentCommandHandler(IGuardianDirectoryStore directory) : ICommandHandler<LinkGuardianStudentCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(LinkGuardianStudentCommand command, CancellationToken cancellationToken)
    {
        await directory.LinkAsync(new GuardianId(command.GuardianId), new StudentId(command.StudentId), cancellationToken);
        return Unit.Value;
    }
}

internal sealed class UnlinkGuardianStudentCommandHandler(IGuardianDirectoryStore directory) : ICommandHandler<UnlinkGuardianStudentCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UnlinkGuardianStudentCommand command, CancellationToken cancellationToken)
    {
        await directory.UnlinkAsync(new GuardianId(command.GuardianId), new StudentId(command.StudentId), cancellationToken);
        return Unit.Value;
    }
}
