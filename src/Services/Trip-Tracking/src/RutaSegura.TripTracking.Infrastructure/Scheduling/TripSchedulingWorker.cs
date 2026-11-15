using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Infrastructure.Options;

namespace RutaSegura.TripTracking.Infrastructure.Scheduling;

/// <summary>
/// Programa los viajes del día (y de los próximos días configurados) a partir de la réplica de rutas.
/// Es idempotente: ScheduleTrip devuelve el viaje existente y la BD tiene índice único (ruta, hora de salida),
/// así que varias réplicas o ejecuciones repetidas no duplican viajes.
/// </summary>
internal sealed class TripSchedulingWorker(
    IServiceScopeFactory scopes,
    IOptions<SchedulingOptions> options,
    TimeProvider clock,
    ILogger<TripSchedulingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var created = await RunOnceAsync(stoppingToken);
                if (created > 0) logger.LogInformation("Programación diaria: {Count} viaje(s) verificados/creados", created);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Programación diaria falló; se reintentará en {Interval}", options.Value.Interval);
            }

            await Task.Delay(options.Value.Interval, clock, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    /// <summary>Recorre las rutas y programa los viajes pendientes. Devuelve cuántos viajes quedaron programados.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RouteSnapshot> routes;
        await using (var scope = scopes.CreateAsyncScope())
            routes = await scope.ServiceProvider.GetRequiredService<IRouteCatalog>().ListAsync(cancellationToken);

        var now = clock.GetUtcNow();
        var scheduled = 0;
        foreach (var route in routes)
        {
            TimeZoneInfo zone;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(route.TimeZoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                logger.LogWarning("Ruta {Route}: zona horaria {Zone} desconocida", route.RouteId, route.TimeZoneId);
                continue;
            }

            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
            for (var day = 0; day <= options.Value.DaysAhead; day++)
            {
                var date = today.AddDays(day);
                if (!route.OperatesOn(date)) continue;
                if (route.ScheduledStartOn(date) < now - options.Value.SkipIfStartedMoreThan) continue;

                await using var scope = scopes.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
                try
                {
                    var result = await dispatcher.SendAsync(new ScheduleTripCommand(route.RouteId, date), cancellationToken);
                    if (result.IsSuccess) scheduled++;
                    else logger.LogWarning("Ruta {Route} {Date}: {Code}", route.RouteId, date, result.FirstError.Code);
                }
                catch (PersistenceConflictException)
                {
                    scheduled++; // otra réplica lo programó en paralelo
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Una ruta mal definida (DomainException) no debe impedir programar las demás.
                    logger.LogWarning(ex, "Ruta {Route} {Date}: no se pudo programar el viaje", route.RouteId, date);
                }
            }
        }

        return scheduled;
    }
}
