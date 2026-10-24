using Monolith.Shared.Domain.Repositories;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;


namespace Monolith.Shared.Infrastructure.Persistence.EFC.Repositories;


/// <summary>
///     Base repository for all repositories
/// </summary>
/// <remarks>
///     This class is used to define the basic CRUD operations for all repositories.
///     This class implements the IBaseRepository interface.
/// </remarks>
/// <typeparam name="TEntity">
///     The entity type for the repository
/// </typeparam>
public class BaseRepository<TEntity> : IBaseRepository<TEntity> where TEntity : class
{
    protected readonly AppDbContext Context;


    /// <summary>
    ///     Default constructor for the base repository
    /// </summary>
    protected BaseRepository(AppDbContext context)
    {
        Context = context;
    }


    // inheritedDoc
    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await Context.Set<TEntity>().AddAsync(entity, cancellationToken);
    }


    // inheritedDoc
    public async Task<TEntity?> FindByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await Context.Set<TEntity>().FindAsync(new object[] { id }, cancellationToken);
    }


    // inheritedDoc
    public void Update(TEntity entity)
    {
        // Aggregates loaded in this context are already tracked: DetectChanges finds what changed, including new
        // child entities (tracked as Added). Calling DbSet.Update on them would mark new children as Modified and
        // turn their INSERT into an UPDATE of a row that does not exist.
        if (Context.Entry(entity).State != EntityState.Detached) return;
        Context.Set<TEntity>().Update(entity);
    }


    // inheritedDoc
    public void Remove(TEntity entity)
    {
        Context.Set<TEntity>().Remove(entity);
    }


    // inheritedDoc
    public virtual async Task<IEnumerable<TEntity>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await Context.Set<TEntity>().ToListAsync(cancellationToken);
    }
}