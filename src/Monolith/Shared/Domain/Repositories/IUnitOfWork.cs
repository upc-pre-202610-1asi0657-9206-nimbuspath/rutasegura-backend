namespace Monolith.Shared.Domain.Repositories;

public interface IUnitOfWork
{
    /// <summary>
    ///     Save changes to the repository
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns></returns>
    Task CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Opens a database transaction for a use case that spans two bounded contexts of the monolith (e.g. a parent
    ///     enrolls a child: Stakeholder saves the enrollment and Route Planning places the child on a stop). Every
    ///     <see cref="CompleteAsync" /> inside it is committed or rolled back together. Disposing it without
    ///     <see cref="IUnitOfWorkTransaction.CommitAsync" /> rolls back. Inside an open transaction it is a no-op.
    /// </summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IUnitOfWorkTransaction>(NoUnitOfWorkTransaction.Instance);
}

/// <summary>Transaction opened by <see cref="IUnitOfWork.BeginTransactionAsync" />.</summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>Used by in-memory units of work (tests) and for nested transactions.</summary>
public sealed class NoUnitOfWorkTransaction : IUnitOfWorkTransaction
{
    public static readonly NoUnitOfWorkTransaction Instance = new();

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
