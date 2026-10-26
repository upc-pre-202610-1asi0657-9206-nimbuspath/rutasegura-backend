using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories;

public sealed class NotificationUnitOfWork(NotificationDbContext context) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Transaccion explicita: tambien revierte INSERTs previos si un UPDATE posterior falla por concurrencia.
            await using var transaction = context.Database.CurrentTransaction is null
                ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new NotificationPersistenceConflictException("NOTIFICATION_CONCURRENT_UPDATE", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: 1062 })
        {
            // No considerar este fallo como exito: reintentar en un scope nuevo y consultar el Inbox antes del ACK.
            throw new NotificationPersistenceConflictException("NOTIFICATION_DUPLICATE_KEY", ex);
        }
    }
}

public sealed class NotificationPersistenceConflictException(string code, Exception innerException)
    : Exception("Conflicto de persistencia; descarta el scope y reevalua la operacion antes de confirmar el mensaje.", innerException)
{
    public string Code { get; } = code;
}
