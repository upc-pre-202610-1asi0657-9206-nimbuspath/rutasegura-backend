using Microsoft.EntityFrameworkCore.Storage;
using Monolith.Shared.Domain.Repositories;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;


namespace Monolith.Shared.Infrastructure.Persistence.EFC.Repositories;


/// <summary>
///     Unit of work for the application.
/// </summary>
/// <remarks>
///     This class is used to save changes to the database context.
///     It implements the IUnitOfWork interface.
/// </remarks>
/// <param name="context">
///     The database context for the application
/// </param>
public class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    // inheritedDoc
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        await context.SaveChangesAsync(cancellationToken);
    }

    // inheritedDoc
    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is not null) return NoUnitOfWorkTransaction.Instance;
        return new EfUnitOfWorkTransaction(await context.Database.BeginTransactionAsync(cancellationToken));
    }

    private sealed class EfUnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            transaction.CommitAsync(cancellationToken);

        // Disposing an uncommitted EF Core transaction rolls it back.
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
