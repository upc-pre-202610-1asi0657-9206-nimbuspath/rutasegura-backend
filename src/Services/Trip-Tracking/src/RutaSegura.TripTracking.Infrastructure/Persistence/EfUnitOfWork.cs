using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.IntegrationEvents;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Trips;
using RutaSegura.TripTracking.Domain.Trips.Events;
using RutaSegura.TripTracking.Infrastructure.LiveUpdates;
using RutaSegura.TripTracking.Infrastructure.Persistence.Configurations;
using RutaSegura.TripTracking.Infrastructure.Persistence.Records;

namespace RutaSegura.TripTracking.Infrastructure.Persistence;

/// <summary>
/// Implementación de <see cref="IUnitOfWork"/> que invoca el <c>TransactionCommandDecorator</c>.
/// En UNA sola transacción MySQL guarda: el agregado, su telemetría y sus domain events traducidos al Outbox.
/// Después del commit ejecuta las acciones efímeras (Redis, canal en vivo).
/// </summary>
internal sealed class EfUnitOfWork(
    TripTrackingDbContext db,
    PostCommitActions afterCommit,
    ILastPositionCache lastPositions,
    ILiveUpdateBroadcaster broadcaster,
    TimeProvider clock,
    ILogger<EfUnitOfWork> logger) : IUnitOfWork
{
    internal static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private static readonly Type[] AggregateParts = [typeof(TripStop), typeof(TripPassenger), typeof(Incident), typeof(SosAlert)];

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.DetectChanges();

        var now = clock.GetUtcNow();
        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        var sequence = 0; // conserva el orden en que el agregado emitió eventos con la misma hora (ej. Started → Delayed)

        foreach (var entry in db.ChangeTracker.Entries<Trip>().ToList())
        {
            var trip = entry.Entity;
            foreach (var domainEvent in trip.DomainEvents)
            {
                var integration = TripIntegrationEventMapper.Map(trip, domainEvent);
                db.OutboxMessages.Add(new OutboxMessage
                {
                    Id = integration.EventId,
                    MessageType = integration.MessageType,
                    SchemaVersion = integration.SchemaVersion,
                    AggregateId = trip.Id.Value,
                    Payload = JsonSerializer.Serialize(integration.Payload, integration.Payload.GetType(), PayloadJson),
                    CorrelationId = correlationId,
                    OccurredAt = integration.OccurredAt,
                    CreatedAt = now.AddMicroseconds(sequence++)
                });

                ScheduleLiveStatus(trip, domainEvent);
            }

            trip.ClearDomainEvents();
        }

        BumpAggregateVersions();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            afterCommit.Discard();
            throw new PersistenceConflictException(PersistenceConflictException.ConcurrencyCode,
                "El viaje fue modificado por otra operación al mismo tiempo. Reintenta la acción.", ex);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            afterCommit.Discard();
            throw new PersistenceConflictException(PersistenceConflictException.DuplicateCode,
                "El registro ya existe (operación repetida).", ex);
        }

        await afterCommit.RunAsync(cancellationToken);
    }

    /// <summary>
    /// Concurrencia optimista por agregado: cualquier cambio en el viaje o en sus partes incrementa
    /// la columna <c>version</c>. Si otra petición confirmó antes, SaveChanges falla y nada se pierde.
    /// </summary>
    private void BumpAggregateVersions()
    {
        var changedParts = db.ChangeTracker.Entries()
            .Where(e => AggregateParts.Contains(e.Entity.GetType()) &&
                        e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => (TripId?)e.Property(TripConfiguration.TripForeignKey).CurrentValue)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

        foreach (var entry in db.ChangeTracker.Entries<Trip>())
        {
            if (entry.State == EntityState.Added) continue;
            if (entry.State != EntityState.Modified && !changedParts.Contains(entry.Entity.Id)) continue;

            var version = entry.Property<int>(TripConfiguration.VersionProperty);
            version.CurrentValue = version.OriginalValue + 1;
        }
    }

    private void ScheduleLiveStatus(Trip trip, IDomainEvent domainEvent)
    {
        var tripId = trip.Id.Value;
        LiveTripStatusUpdate? update = domainEvent switch
        {
            TripStarted e => new(tripId, nameof(TripStatus.InProgress), null, e.OccurredOn),
            TripDelayed e => new(tripId, nameof(TripStatus.Delayed), e.Reason, e.OccurredOn),
            TripCompleted e => new(tripId, nameof(TripStatus.Completed), null, e.OccurredOn),
            TripCancelled e => new(tripId, nameof(TripStatus.Cancelled), e.Reason, e.OccurredOn),
            _ => null
        };
        if (update is null) return;

        afterCommit.Enqueue("broadcast-status", ct => broadcaster.BroadcastStatusAsync(update, ct));
        if (domainEvent is TripCompleted or TripCancelled)
        {
            // "Dejaste de compartir tu ubicación": la posición deja de estar disponible.
            afterCommit.Enqueue("evict-last-position", ct => lastPositions.RemoveAsync(new TripId(tripId), ct));
        }
    }

    private bool IsDuplicateKey(DbUpdateException ex)
    {
        for (Exception? inner = ex; inner is not null; inner = inner.InnerException)
        {
            if (inner is MySqlException { Number: 1062 })
            {
                logger.LogInformation("Clave duplicada al confirmar: {Message}", inner.Message);
                return true;
            }
        }

        return false;
    }
}
