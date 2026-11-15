using RutaSegura.BuildingBlocks.Application.Persistence;

namespace RutaSegura.BuildingBlocks.Testing;

/// <summary>Doble genérico; no implementa transacciones ni persistencia real.</summary>
public sealed class RecordingUnitOfWork(Func<CancellationToken, Task>? beforeCommit = null) : IUnitOfWork
{
    private int _commits;
    public int Commits => Volatile.Read(ref _commits);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (beforeCommit is not null)
            await beforeCommit(cancellationToken);
        Interlocked.Increment(ref _commits);
    }
}
