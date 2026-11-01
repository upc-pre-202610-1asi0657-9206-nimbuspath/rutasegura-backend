using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RutaSegura.BuildingBlocks.Messaging;
using RutaSegura.Notification.Infrastructure.Messaging.Inbound;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Infrastructure.LiveUpdates;
using RutaSegura.TripTracking.Infrastructure.Messaging.Outbox;
using RutaSegura.TripTracking.Infrastructure.Messaging.RabbitMq;
using RutaSegura.TripTracking.Infrastructure.Options;
using RutaSegura.TripTracking.Infrastructure.Persistence;

namespace RutaSegura.Notification.Messaging.Tests;

public sealed class BrokerIntegrationTests : BrokerTestContext
{
    [BrokerFact]
    public async Task ConsumerResubscribesAfterServerCancellationWhileTheConnectionStaysOpen()
    {
        var connection = Services.GetRequiredService<RutaSegura.Notification.Infrastructure.Messaging.RabbitMq.NotificationRabbitMqConnection>();
        var current = await connection.GetAsync(Ct);
        await Channel.QueueDeleteAsync(Options.Queue, false, false, cancellationToken: Ct);
        // El consumidor vuelve a declarar su topologia y a suscribirse sin depender de un corte TCP.
        await Eventually(async () =>
        {
            await using var probe = await Broker.CreateChannelAsync(cancellationToken: Ct);
            try { return (await probe.QueueDeclarePassiveAsync(Options.Queue, Ct)).ConsumerCount == 1; }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException) { return false; }
        });
        Worker.IsConsuming.ShouldBeTrue();
        (await connection.GetAsync(Ct)).ShouldBeSameAs(current);
        await Publish(MessageSamples.Message());
        await Eventually(async () => { await using var db = OpenNotification(); return await db.ProcessedEvents.CountAsync(Ct) == 1; });
    }

    [BrokerFact]
    public async Task ConcurrentDuplicateConsumersReevaluateInCleanScopesAndPersistOnlyOneNotice()
    {
        var processor = Services.GetRequiredService<INotificationMessageProcessor>();
        var input = MessageSamples.Message();
        var results = await Task.WhenAll(processor.ProcessAsync(input, Ct), processor.ProcessAsync(input, Ct));
        results.ShouldContain(r => r.Outcome == NotificationConsumeOutcome.Committed);
        results.ShouldNotContain(r => r.Outcome == NotificationConsumeOutcome.Rejected);
        // Si hubo una carrera del indice unico, se reevalua contra el Inbox ya confirmado.
        foreach (var retry in results.Where(r => r.Outcome == NotificationConsumeOutcome.Retry))
            (await processor.ProcessAsync(input, Ct)).Outcome.ShouldBe(NotificationConsumeOutcome.Committed);
        await using var verify = OpenNotification();
        (await verify.ProcessedEvents.CountAsync(Ct)).ShouldBe(1);
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(1);
        (await verify.DispatchJobs.CountAsync(Ct)).ShouldBe(1);
    }

    [BrokerFact]
    public async Task MandatoryTransferFailsWhenDlqBindingIsMissing_InsteadOfPretendingSuccess()
    {
        await Channel.QueueUnbindAsync(Options.ErrorQueue, Options.ErrorExchange, Options.Queue, cancellationToken: Ct);
        await using var channel = await Broker.CreateChannelAsync(new RabbitMQ.Client.CreateChannelOptions(true, true), Ct);
        await using var publisher = new RutaSegura.Notification.Infrastructure.Messaging.RabbitMq.NotificationFailurePublisher(Options, channel);
        await Should.ThrowAsync<RabbitMQ.Client.Exceptions.PublishException>(() => publisher.ForwardAsync(MessageSamples.Message(), null, "test", Ct));
    }

    [BrokerFact]
    public async Task ConsumerReconnectsAfterConnectionLossAndRetainsIdempotency()
    {
        var connection = Services.GetRequiredService<RutaSegura.Notification.Infrastructure.Messaging.RabbitMq.NotificationRabbitMqConnection>();
        var current = await connection.GetAsync(Ct);
        await current.CloseAsync();
        var input = MessageSamples.Message();
        await Publish(input);
        await Publish(input);
        await Eventually(async () => { await using var db = OpenNotification(); return await db.ProcessedEvents.CountAsync(Ct) == 1; });
        await using var verify = OpenNotification();
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(1);
        (await connection.GetAsync(Ct)).ShouldNotBeSameAs(current);
    }

    [BrokerFact]
    public async Task SixWireEventsPersistInboxNoticesAndPendingDispatchAndDuplicateNothing()
    {
        var messages = RutaSegura.Notification.Infrastructure.Messaging.RabbitMq.NotificationRabbitMqTopology.RoutingKeys
            .Select(type => MessageSamples.Message(type)).ToArray();
        foreach (var message in messages) { await Publish(message); await Publish(message); }
        await Eventually(async () => { await using var db = OpenNotification(); return await db.ProcessedEvents.CountAsync(Ct) == 6; });
        await Eventually(async () => await Channel.MessageCountAsync(Options.Queue, Ct) == 0 &&
            (await Channel.QueueDeclarePassiveAsync(Options.Queue, Ct)).ConsumerCount == 1);
        await using var verify = OpenNotification();
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(7); // Cinco avisos + SOS tutor y operador.
        (await verify.DispatchJobs.CountAsync(Ct)).ShouldBe(7);
        (await verify.DispatchJobs.AnyAsync(j => j.ExpandedAt != null, Ct)).ShouldBeFalse();
        (await verify.Deliveries.CountAsync(Ct)).ShouldBe(0); // Expandir por FCM/SignalR pertenece a la fase de canales.
        (await Channel.MessageCountAsync(Options.ErrorQueue, Ct)).ShouldBe(0u);
        Directory.Calls.ShouldBe(6); // El Inbox evita incluso repetir consultas a terceros.
    }

    [BrokerFact]
    public async Task TransientDependencyFailureRetriesThenCommitsOneLogicalNotice()
    {
        Directory.FailuresRemaining = 1;
        var message = MessageSamples.Message();
        await Publish(message);
        await Eventually(async () => { await using var db = OpenNotification(); return await db.ProcessedEvents.CountAsync(Ct) == 1; });
        await using var verify = OpenNotification();
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(1);
        (await verify.DispatchJobs.CountAsync(Ct)).ShouldBe(1);
        Directory.Calls.ShouldBe(2);
        (await Channel.MessageCountAsync(Options.ErrorQueue, Ct)).ShouldBe(0u);
    }

    [BrokerFact]
    public async Task ExhaustedRetriesEndInDlqWithOriginalIdentityAndNoPartialDatabaseState()
    {
        Directory.FailuresRemaining = 100;
        var message = MessageSamples.Message();
        await Publish(message);
        var dlq = await DeadLetter();
        dlq.BasicProperties.MessageId.ShouldBe(message.MessageId);
        dlq.BasicProperties.CorrelationId.ShouldBe(message.CorrelationId);
        dlq.Body.ToArray().ShouldBe(message.Body.ToArray());
        TripTrackingMessageDecoder.ReadHeader(dlq.BasicProperties.Headers, "x-notification-error-code").ShouldBe("NOTIFICATION_RETRIES_EXHAUSTED");
        Directory.Calls.ShouldBe(3); // Inicial + dos reintentos configurados para pruebas.
        await using var verify = OpenNotification();
        (await verify.ProcessedEvents.CountAsync(Ct)).ShouldBe(0);
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(0);
        (await verify.DispatchJobs.CountAsync(Ct)).ShouldBe(0);
    }

    [BrokerFact]
    public async Task InvalidPayloadIsQuarantinedWithoutInvokingBusinessDependencies()
    {
        var message = MessageSamples.Message() with { Body = "{"u8.ToArray() };
        await Publish(message);
        var dlq = await DeadLetter();
        dlq.BasicProperties.MessageId.ShouldBe(message.MessageId);
        Directory.Calls.ShouldBe(0);
        await using var verify = OpenNotification();
        (await verify.ProcessedEvents.CountAsync(Ct)).ShouldBe(0);
    }

    [BrokerFact]
    public async Task SameMessageIdWithDifferentBusinessContentIsNotSilentlyAcknowledged()
    {
        var original = MessageSamples.Message();
        await Publish(original);
        await Eventually(async () => { await using var db = OpenNotification(); return await db.ProcessedEvents.CountAsync(Ct) == 1; });
        var changed = MessageSamples.Message(id: Guid.Parse(original.MessageId!), payload: new { organizationId = MessageSamples.Organization,
            tripId = MessageSamples.Trip, studentId = Guid.NewGuid(), stopId = MessageSamples.Stop });
        await Publish(changed);
        var dlq = await DeadLetter();
        TripTrackingMessageDecoder.ReadHeader(dlq.BasicProperties.Headers, "x-notification-error-code").ShouldBe("NOTIFICATION_SOURCE_CONFLICT");
        await using var verify = OpenNotification();
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(1);
        (await verify.ProcessedEvents.CountAsync(Ct)).ShouldBe(1);
    }

    [BrokerFact]
    public async Task ReturnCompletionAndInactiveProximityAreRecordedAsSkipped()
    {
        Directory.Active = false;
        await Publish(MessageSamples.Message("trip-tracking.vehicle-approaching-stop"));
        await Publish(MessageSamples.Message("trip-tracking.trip-completed", payload: new { organizationId = MessageSamples.Organization,
            tripId = MessageSamples.Trip, routeId = MessageSamples.Route, direction = "Return", schoolDeliveredStudentIds = Array.Empty<Guid>() }));
        await Eventually(async () => { await using var db = OpenNotification(); return await db.ProcessedEvents.CountAsync(Ct) == 2; });
        await using var verify = OpenNotification();
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(0);
        (await verify.ProcessedEvents.Select(e => e.SkippedReason).ToListAsync(Ct))
            .ShouldBe(["RETURN_DELIVERY_REPORTED_BY_ALIGHTING", "TRIP_NOT_ACTIVE"], ignoreOrder: true);
    }

    [BrokerFact]
    public async Task CommitFollowedByLostAckAndBrokerRedeliveryIsIdempotent()
    {
        var message = MessageSamples.Message();
        var processor = Services.GetRequiredService<INotificationMessageProcessor>();
        // Simula commit correcto y caida antes del ACK; RabbitMQ vuelve a entregar el original.
        (await processor.ProcessAsync(message, Ct)).Outcome.ShouldBe(NotificationConsumeOutcome.Committed);
        await Publish(message);
        await Publish(message);
        await Eventually(async () => await Channel.MessageCountAsync(Options.Queue, Ct) == 0);
        await using var verify = OpenNotification();
        (await verify.Notifications.CountAsync(Ct)).ShouldBe(1);
        (await verify.ProcessedEvents.CountAsync(Ct)).ShouldBe(1);
        (await verify.DispatchJobs.CountAsync(Ct)).ShouldBe(1);
    }

    [BrokerFact]
    public async Task ActualTripUnitOfWorkOutboxRelayAndNotificationConsumerWorkAcrossSeparateDatabases()
    {
        var now = DateTimeOffset.UtcNow;
        var driver = new DriverId(Guid.NewGuid());
        var trip = Trip.Schedule(new TripId(MessageSamples.Trip), new RouteId(MessageSamples.Route), "Ruta de prueba", TripDirection.Pickup,
            driver, new VehicleId(Guid.NewGuid()), now, [new(new StopId(MessageSamples.Stop), 1, "Parada", GeoPosition.Create(-12.1, -77))],
            [new(new StudentId(MessageSamples.Student), new StopId(MessageSamples.Stop))], now, new OrganizationId(MessageSamples.Organization));
        trip.Start(driver, ClientEventId.New(), now);
        trip.RecordBoarding(driver, new StudentId(MessageSamples.Student), ClientEventId.New(), now, null, now);
        trip.ClearDomainEvents(); // Solo queremos el evento nuevo de cierre en este escenario.
        trip.Complete(driver, ClientEventId.New(), now.AddSeconds(1));
        Guid sourceId;
        await using (var db = OpenTrip())
        {
            db.Trips.Add(trip);
            await using (var before = OpenTrip()) (await before.OutboxMessages.CountAsync(Ct)).ShouldBe(0);
            var ephemeral = new EphemeralPorts();
            var uow = new EfUnitOfWork(db, new PostCommitActions(NullLogger<PostCommitActions>.Instance), ephemeral, ephemeral,
                TimeProvider.System, NullLogger<EfUnitOfWork>.Instance);
            await uow.SaveChangesAsync(Ct);
            var row = await db.OutboxMessages.SingleAsync(Ct);
            row.ProcessedAt.ShouldBeNull();
            sourceId = row.Id;
        }
        await using (var beforeRelay = OpenNotification()) (await beforeRelay.Notifications.CountAsync(Ct)).ShouldBe(0);
        var registrations = new ServiceCollection();
        registrations.AddDbContext<TripTrackingDbContext>(o => o.UseMySQL(TripConnection));
        await using var scopes = registrations.BuildServiceProvider();
        await using var connection = new RabbitMqConnection(Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions
        {
            HostName = Options.HostName, Port = Options.Port, UserName = Options.UserName, Password = Options.Password,
            EventsExchange = Options.EventsExchange
        }), NullLogger<RabbitMqConnection>.Instance);
        await using var publisher = new RabbitMqMessagePublisher(connection, NullLogger<RabbitMqMessagePublisher>.Instance);
        using var relay = new OutboxProcessor(scopes.GetRequiredService<IServiceScopeFactory>(), publisher,
            Microsoft.Extensions.Options.Options.Create(new OutboxOptions()), TimeProvider.System, NullLogger<OutboxProcessor>.Instance);
        (await relay.ProcessBatchAsync(Ct)).ShouldBe(1);
        await Eventually(async () => { await using var db = OpenNotification(); return await db.ProcessedEvents.AnyAsync(e => e.MessageId == sourceId, Ct); });
        await using var tracking = OpenTrip();
        (await tracking.OutboxMessages.SingleAsync(Ct)).ProcessedAt.ShouldNotBeNull();
        await using var notifications = OpenNotification();
        (await notifications.Notifications.SingleAsync(Ct)).SourceMessageId.ShouldBe(sourceId);
        (await notifications.DispatchJobs.CountAsync(Ct)).ShouldBe(1);
        (await relay.ProcessBatchAsync(Ct)).ShouldBe(0);
    }

    [BrokerFact]
    public async Task OutboxFailureKeepsStableIdAndSchedulesAnotherAttemptWithoutMarkingPublished()
    {
        var id = Guid.NewGuid();
        var registrations = new ServiceCollection();
        registrations.AddDbContext<TripTrackingDbContext>(o => o.UseMySQL(TripConnection));
        await using var scopes = registrations.BuildServiceProvider();
        await using (var db = OpenTrip())
        {
            db.OutboxMessages.Add(new() { Id = id, AggregateId = MessageSamples.Trip, MessageType = "trip-tracking.student-boarded", SchemaVersion = 1,
                CorrelationId = "outbox-test", Payload = "{}", OccurredAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(Ct);
        }
        using var relay = new OutboxProcessor(scopes.GetRequiredService<IServiceScopeFactory>(), new FailingPublisher(),
            Microsoft.Extensions.Options.Options.Create(new OutboxOptions()), TimeProvider.System, NullLogger<OutboxProcessor>.Instance);
        (await relay.ProcessBatchAsync(Ct)).ShouldBe(1);
        await using var verify = OpenTrip();
        var row = await verify.OutboxMessages.SingleAsync(Ct);
        row.Id.ShouldBe(id);
        row.ProcessedAt.ShouldBeNull();
        row.Attempts.ShouldBe(1);
        row.NextAttemptAt.ShouldNotBeNull();
    }

    private sealed class FailingPublisher : IMessagePublisher
    {
        public Task PublishAsync<TPayload>(IntegrationMessage<TPayload> message, CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("Broker temporalmente no disponible (doble de prueba)."));
    }
    private sealed class EphemeralPorts : ILastPositionCache, ILiveUpdateBroadcaster
    {
        public Task<LivePositionUpdate?> GetAsync(TripId tripId, CancellationToken cancellationToken) => Task.FromResult<LivePositionUpdate?>(null);
        public Task SetAsync(LivePositionUpdate update, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveAsync(TripId tripId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task BroadcastPositionAsync(LivePositionUpdate update, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task BroadcastStatusAsync(LiveTripStatusUpdate update, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
