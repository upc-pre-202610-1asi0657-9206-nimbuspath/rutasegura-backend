using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.BuildingBlocks.Testing;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Infrastructure.Messaging.Inbound;
using RutaSegura.TripTracking.Infrastructure.Messaging.Outbox;
using RutaSegura.TripTracking.Infrastructure.Messaging.RabbitMq;
using RutaSegura.TripTracking.Infrastructure.Options;
using RutaSegura.TripTracking.Infrastructure.Persistence;
using RutaSegura.TripTracking.Infrastructure.Scheduling;
using RutaSegura.TripTracking.Integration.Tests.Infrastructure;

namespace RutaSegura.TripTracking.Integration.Tests.Persistence;

/// <summary>Adaptadores de Infrastructure contra MySQL real: concurrencia, unicidad, Outbox, Inbox y programación.</summary>
[Trait("Category", "Integration")]
public sealed class PersistenceAndMessagingTests(TripTrackingApiFactory factory) : IClassFixture<TripTrackingApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConcurrentChangesToTheSameTrip_SecondCommitGetsConcurrencyConflict()
    {
        factory.RequireMySql();
        factory.Clock.SetUtcNow(TripTrackingApiFactory.Monday0640);
        var seeded = await factory.SeedTripAsync();
        var driver = new DriverId(seeded.DriverId);

        await using var first = factory.Services.CreateAsyncScope();
        await using var second = factory.Services.CreateAsyncScope();
        var tripA = (await first.ServiceProvider.GetRequiredService<ITripRepository>().GetByIdAsync(new TripId(seeded.TripId), Ct))!;
        var tripB = (await second.ServiceProvider.GetRequiredService<ITripRepository>().GetByIdAsync(new TripId(seeded.TripId), Ct))!;

        tripA.Start(driver, ClientEventId.New(), TripTrackingApiFactory.Monday0640);
        tripB.Cancel("Cambio de último minuto", TripTrackingApiFactory.Monday0640);

        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        var conflict = await Should.ThrowAsync<PersistenceConflictException>(
            () => second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct));

        conflict.Code.ShouldBe(PersistenceConflictException.ConcurrencyCode);
        await using var check = factory.Services.CreateAsyncScope();
        var stored = await check.ServiceProvider.GetRequiredService<ITripRepository>().GetByIdAsync(new TripId(seeded.TripId), Ct);
        stored!.Status.ShouldBe(TripStatus.InProgress); // ganó el primero; el cambio perdido no se mezcla
    }

    [Fact]
    public async Task SameRouteAndStartTwice_IsRejectedByTheUniqueIndex()
    {
        factory.RequireMySql();
        var seeded = await factory.SeedTripAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITripRepository>();
        var original = (await repository.GetByIdAsync(new TripId(seeded.TripId), Ct))!;

        repository.Add(Trip.Schedule(TripId.New(), original.RouteId, original.RouteName, original.Direction, original.DriverId,
            original.VehicleId, original.ScheduledStart,
            original.Stops.Select(s => new TripStopDefinition(s.StopId, s.Sequence, s.Name, s.Position)).ToList(),
            [], TripTrackingApiFactory.Monday0640));

        var duplicate = await Should.ThrowAsync<PersistenceConflictException>(
            () => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct));
        duplicate.Code.ShouldBe(PersistenceConflictException.DuplicateCode);
    }

    [Fact]
    public async Task AggregateRoundTrip_PreservesValueObjectsAndIdempotencyKeys()
    {
        factory.RequireMySql();
        factory.Clock.SetUtcNow(TripTrackingApiFactory.Monday0640);
        var seeded = await factory.SeedTripAsync(direction: TripDirection.Return);
        var start = ClientEventId.New();
        var sos = ClientEventId.New();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var trip = (await scope.ServiceProvider.GetRequiredService<ITripRepository>().GetByIdAsync(new TripId(seeded.TripId), Ct))!;
            trip.Start(new DriverId(seeded.DriverId), start, TripTrackingApiFactory.Monday0640);
            trip.RecordBoarding(new DriverId(seeded.DriverId), new StudentId(seeded.Student1), ClientEventId.New(), null,
                Domain.Tracking.GeoPosition.Create(-12.08, -77.0), TripTrackingApiFactory.Monday0640);
            trip.RaiseSos(new DriverId(seeded.DriverId), sos, null, null, TripTrackingApiFactory.Monday0640);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        await using var read = factory.Services.CreateAsyncScope();
        var stored = (await read.ServiceProvider.GetRequiredService<ITripRepository>().GetByIdAsync(new TripId(seeded.TripId), Ct))!;
        stored.Direction.ShouldBe(TripDirection.Return);
        stored.Passengers.Single(p => p.StudentId == new StudentId(seeded.Student1)).BoardedPosition!.Latitude.ShouldBe(-12.08);
        stored.ActiveSos.ShouldNotBeNull().Position.ShouldBeNull(); // SOS sin GPS
        stored.ProcessedClientEvents.ShouldContain(start);
        stored.ProcessedClientEvents.ShouldContain(sos);
        stored.ScheduledStart.ShouldBe(TripTrackingApiFactory.Monday0640);
        stored.Start(new DriverId(seeded.DriverId), start, TripTrackingApiFactory.Monday0640).ShouldBeFalse(); // reintento tras recargar
    }

    [Fact]
    public async Task OutboxProcessor_PublishesPendingEventsOnceUsingEventIdAsMessageId()
    {
        factory.RequireMySql();
        var seeded = await factory.SeedTripAsync();
        var publisher = new RecordingMessagePublisher();
        var processor = new OutboxProcessor(factory.Services.GetRequiredService<IServiceScopeFactory>(), publisher,
            Options.Create(new OutboxOptions { BatchSize = 500 }), factory.Clock, NullLogger<OutboxProcessor>.Instance);

        while (await processor.ProcessBatchAsync(Ct) > 0) { }
        var again = await processor.ProcessBatchAsync(Ct);

        again.ShouldBe(0);
        var scheduled = publisher.Messages.Where(m => m.MessageType == "trip-tracking.trip-scheduled").ToList();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripTrackingDbContext>();
        var outboxId = await db.OutboxMessages.Where(m => m.AggregateId == seeded.TripId).Select(m => m.Id).SingleAsync(Ct);
        scheduled.ShouldContain(m => m.MessageId == outboxId);
        (await db.OutboxMessages.CountAsync(m => m.ProcessedAt == null, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task InboundConsumer_IsIdempotentAndSendsInvalidMessagesToTheDlq()
    {
        factory.RequireMySql();
        var consumer = new InboundEventsConsumer(
            new RabbitMqConnection(Options.Create(new RabbitMqOptions()), NullLogger<RabbitMqConnection>.Instance),
            factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Clock, NullLogger<InboundEventsConsumer>.Instance);
        var routeId = Guid.NewGuid();
        var stop = Guid.NewGuid();
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            routeId, organizationId = Guid.NewGuid(), name = "Ruta 9", direction = "Pickup", driverId = Guid.NewGuid(), vehicleId = Guid.NewGuid(),
            departureTime = "07:10", timeZoneId = "America/Lima", operatingDays = new[] { "Monday", "Friday" },
            stops = new[] { new { stopId = stop, sequence = 1, name = "Parada 1", latitude = -12.1, longitude = -77.0 } },
            students = new[] { new { studentId = Guid.NewGuid(), stopId = stop } }
        }));
        var messageId = Guid.NewGuid().ToString();

        (await consumer.HandleAsync(messageId, "administration.route-published", body, false, Ct)).ShouldBe(InboundOutcome.Processed);
        (await consumer.HandleAsync(messageId, "administration.route-published", body, true, Ct)).ShouldBe(InboundOutcome.Duplicate);
        (await consumer.HandleAsync(Guid.NewGuid().ToString(), "administration.route-published", "{\"routeId\":1}"u8.ToArray(), false, Ct))
            .ShouldBe(InboundOutcome.Rejected);
        (await consumer.HandleAsync(Guid.NewGuid().ToString(), "administration.vehicle-registered", "{}"u8.ToArray(), false, Ct))
            .ShouldBe(InboundOutcome.Ignored);
        (await consumer.HandleAsync(null, "administration.route-published", body, false, Ct)).ShouldBe(InboundOutcome.Rejected);

        await using var scope = factory.Services.CreateAsyncScope();
        var route = await scope.ServiceProvider.GetRequiredService<IRouteCatalog>().GetAsync(new RouteId(routeId), Ct);
        route.ShouldNotBeNull().DepartureTime.ShouldBe(new TimeOnly(7, 10));
        route.OperatingDays.ShouldBe([DayOfWeek.Monday, DayOfWeek.Friday], ignoreOrder: true);
    }

    [Fact]
    public async Task SchedulingWorker_CreatesTodaysTripsOnceForOperatingRoutes()
    {
        factory.RequireMySql();
        factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 7, 5, 0, 0, TripTrackingApiFactory.Lima)); // miércoles 05:00
        var seeded = await factory.SeedTripAsync(); // la ruta opera todos los días a las 06:40
        var worker = new TripSchedulingWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SchedulingOptions { DaysAhead = 1 }), factory.Clock, NullLogger<TripSchedulingWorker>.Instance);

        await worker.RunOnceAsync(Ct);
        await worker.RunOnceAsync(Ct); // idempotente

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripTrackingDbContext>();
        var starts = await db.Trips.Where(t => t.RouteId == new RouteId(seeded.RouteId)).Select(t => t.ScheduledStart).ToListAsync(Ct);
        starts.Count.ShouldBe(3); // lunes (seed) + miércoles + jueves
        starts.ShouldContain(new DateTimeOffset(2026, 10, 7, 6, 40, 0, TripTrackingApiFactory.Lima));
        starts.ShouldContain(new DateTimeOffset(2026, 10, 8, 6, 40, 0, TripTrackingApiFactory.Lima));
    }
}
