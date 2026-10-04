namespace RutaSegura.BuildingBlocks.Application.Persistence;

/// <summary>
/// Puerto de salida. En Infrastructure lo implementa el DbContext de EF Core: al guardar,
/// un interceptor mueve los domain events de los agregados a la tabla Outbox (misma transacción).
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
