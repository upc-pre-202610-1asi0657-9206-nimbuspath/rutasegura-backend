namespace Monolith.Shared.Domain.Repositories;

/// <summary>
///     Base repository interface for all repositories
/// </summary>
/// <remarks>
///     This interface is used to define the basic CRUD operations for all repositories
/// </remarks>
/// <typeparam name="TEntity">
///     The entity type for the repository
/// </typeparam>
public interface IBaseRepository<TEntity>
{
    /// <summary>
    ///     Add an entity to the repository
    /// </summary>
    /// <param name="entity">
    ///     The entity to add
    /// </param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns></returns>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);


    /// <summary>
    ///     Find an entity by its id
    /// </summary>
    /// <param name="id">
    ///     The id of the entity to find
    /// </param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>
    ///     The entity if found, otherwise null
    /// </returns>
    Task<TEntity?> FindByIdAsync(int id, CancellationToken cancellationToken = default);


    void Update(TEntity entity);


    /// <summary>
    ///     Remove an entity from the repository
    /// </summary>
    /// <param name="entity">
    ///     The entity to remove
    /// </param>
    void Remove(TEntity entity);


    /// <summary>
    ///     List all entities in the repository
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>
    ///     A list of all entities in the repository
    /// </returns>
    Task<IEnumerable<TEntity>> ListAsync(CancellationToken cancellationToken = default);
}