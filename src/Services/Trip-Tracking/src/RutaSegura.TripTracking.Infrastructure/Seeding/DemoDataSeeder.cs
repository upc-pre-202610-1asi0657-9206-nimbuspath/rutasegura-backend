using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Replicas;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Infrastructure.Persistence;

namespace RutaSegura.TripTracking.Infrastructure.Seeding;

/// <summary>
/// Datos del prototipo "RutaSegura Móvil Conductor" (Ruta 3, Colegio San Agustín, conductor Jorge Mendoza).
/// Solo para desarrollo, demos y Postman mientras Administration e IAM no publican sus eventos.
/// </summary>
public static class DemoData
{
    public static readonly Guid OrganizationId = Guid.Parse("0a000000-0000-0000-0000-000000000001"); // Transportes Santa Rosa
    public static readonly Guid AdminId = Guid.Parse("0a000000-0000-0000-0000-0000000000ad");
    public static readonly Guid DriverId = Guid.Parse("11111111-1111-1111-1111-111111111111");          // Jorge Mendoza
    public static readonly Guid VehicleId = Guid.Parse("33333333-3333-3333-3333-333333333333");         // ABC-123
    public static readonly Guid PickupRouteId = Guid.Parse("44444444-4444-4444-4444-444444444444");     // Ruta 3, recojo
    public static readonly Guid ReturnRouteId = Guid.Parse("44444444-4444-4444-4444-444444444445");     // Ruta 3, retorno

    public static readonly Guid Stop1 = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid Stop2 = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    public static readonly Guid School = Guid.Parse("a0000000-0000-0000-0000-000000000003");

    public static readonly Guid Valeria = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    public static readonly Guid Mateo = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    public static readonly Guid Camila = Guid.Parse("b0000000-0000-0000-0000-000000000003");

    public static readonly Guid GuardianOfValeria = Guid.Parse("c0000000-0000-0000-0000-000000000001");

    private static readonly DayOfWeek[] EveryDay = Enum.GetValues<DayOfWeek>();

    public static RouteSnapshot PickupRoute() => new(
        PickupRouteId, "Ruta 3", TripDirection.Pickup, DriverId, VehicleId, new TimeOnly(6, 40), "America/Lima", EveryDay,
        [
            new RouteStopSnapshot(Stop1, 1, "Av. Primavera 1240", -12.1000, -77.0000),
            new RouteStopSnapshot(Stop2, 2, "Jr. Las Begonias 310", -12.0900, -77.0000),
            new RouteStopSnapshot(School, 3, "Colegio San Agustín", -12.0800, -77.0000)
        ],
        [new RouteStudentSnapshot(Valeria, Stop1), new RouteStudentSnapshot(Mateo, Stop1), new RouteStudentSnapshot(Camila, Stop2)],
        OrganizationId);

    public static RouteSnapshot ReturnRoute() => new(
        ReturnRouteId, "Ruta 3, retorno", TripDirection.Return, DriverId, VehicleId, new TimeOnly(13, 30), "America/Lima", EveryDay,
        [
            new RouteStopSnapshot(School, 1, "Colegio San Agustín", -12.0800, -77.0000),
            new RouteStopSnapshot(Stop2, 2, "Jr. Las Begonias 310", -12.0900, -77.0000),
            new RouteStopSnapshot(Stop1, 3, "Av. Primavera 1240", -12.1000, -77.0000)
        ],
        [new RouteStudentSnapshot(Valeria, Stop1), new RouteStudentSnapshot(Mateo, Stop1), new RouteStudentSnapshot(Camila, Stop2)],
        OrganizationId);
}

/// <summary>Carga <see cref="DemoData"/> al arrancar si <c>Seed:Demo=true</c>. Espera a que MySQL esté disponible.</summary>
internal sealed class DemoDataSeeder(IServiceScopeFactory scopes, TimeProvider clock, ILogger<DemoDataSeeder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= 30 && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await SeedAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Datos demo: intento {Attempt} falló (¿MySQL listo y migrado?)", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), clock, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    internal async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using (var check = scopes.CreateAsyncScope())
        {
            var db = check.ServiceProvider.GetRequiredService<TripTrackingDbContext>();
            if (!await db.Database.CanConnectAsync(cancellationToken))
                throw new InvalidOperationException("MySQL todavía no responde.");
        }

        await SendAsync(new UpsertRouteSnapshotCommand(DemoData.PickupRoute()), cancellationToken);
        await SendAsync(new UpsertRouteSnapshotCommand(DemoData.ReturnRoute()), cancellationToken);
        await SendAsync(new LinkGuardianStudentCommand(DemoData.GuardianOfValeria, DemoData.Valeria), cancellationToken);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),
            TimeZoneInfo.FindSystemTimeZoneById("America/Lima")).DateTime);
        foreach (var routeId in new[] { DemoData.PickupRouteId, DemoData.ReturnRouteId })
        {
            try
            {
                await SendAsync(new ScheduleTripCommand(routeId, today), cancellationToken);
            }
            catch (PersistenceConflictException)
            {
                // ya existía (otra réplica)
            }
        }

        logger.LogInformation("Datos demo listos: Ruta 3 (recojo 06:40 y retorno 13:30) para {Date}", today);
    }

    private async Task SendAsync<T>(ICommand<T> command, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, cancellationToken);
        if (result.IsFailure)
            logger.LogWarning("Datos demo: {Command} → {Code} {Message}", command.GetType().Name, result.FirstError.Code, result.FirstError.Message);
    }
}
