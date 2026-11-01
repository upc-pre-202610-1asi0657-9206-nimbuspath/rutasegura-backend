using Microsoft.EntityFrameworkCore;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Subscription.Application.Internal.OutboundServices;
using Monolith.Subscription.Domain.Model.Aggregates;
using Monolith.Subscription.Domain.Model.ValueObjects;
using Monolith.Subscription.Domain.Repositories;

namespace Monolith.Subscription.Infrastructure.Persistence.EFC.Repositories;

public class PlanRepository(AppDbContext context) : IPlanRepository
{
    public Task<Plan?> FindByCodeAsync(PlanCode code, CancellationToken cancellationToken) =>
        context.Set<Plan>().FirstOrDefaultAsync(plan => plan.Code == code, cancellationToken);

    public async Task<Plan> GetDefaultAsync(CancellationToken cancellationToken) =>
        await context.Set<Plan>().FirstOrDefaultAsync(plan => plan.IsDefault, cancellationToken) ??
        throw new InvalidOperationException("The plan catalog has no default plan; apply the migrations.");

    public async Task<IReadOnlyList<Plan>> ListAvailableAsync(CancellationToken cancellationToken) =>
        await context.Set<Plan>().AsNoTracking().Where(plan => plan.IsAvailable)
            .OrderBy(plan => plan.MonthlyPriceInCents).ThenBy(plan => plan.MaxActiveRoutes)
            .ToListAsync(cancellationToken);
}

public class TenantSubscriptionRepository(AppDbContext context) : ITenantSubscriptionRepository
{
    public Task<TenantSubscription?> FindByOrganizationAsync(OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        context.Set<TenantSubscription>().Include(s => s.PlanChanges)
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);

    public Task<TenantSubscription?> FindByPaymentReferenceAsync(string paymentReference,
        CancellationToken cancellationToken) =>
        context.Set<TenantSubscription>().Include(s => s.PlanChanges)
            .FirstOrDefaultAsync(s => s.PlanChanges.Any(c => c.PaymentReference == paymentReference),
                cancellationToken);

    public async Task AddAsync(TenantSubscription subscription, CancellationToken cancellationToken) =>
        await context.Set<TenantSubscription>().AddAsync(subscription, cancellationToken);

    /// <remarks>
    ///     A loaded contract is already tracked: new plan changes are found by DetectChanges and inserted. Calling
    ///     DbSet.Update on it would mark those new changes as Modified (their ids are assigned by the domain) and the
    ///     save would fail, so only a detached contract is attached.
    /// </remarks>
    public void Update(TenantSubscription subscription)
    {
        if (context.Entry(subscription).State == EntityState.Detached)
            context.Set<TenantSubscription>().Update(subscription);
    }
}

public class ProcessedPaymentEventRepository(AppDbContext context) : IProcessedPaymentEventRepository
{
    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken) =>
        context.Set<ProcessedPaymentEvent>().AnyAsync(e => e.EventId == eventId, cancellationToken);

    public async Task AddAsync(ProcessedPaymentEvent paymentEvent, CancellationToken cancellationToken) =>
        await context.Set<ProcessedPaymentEvent>().AddAsync(paymentEvent, cancellationToken);
}

/// <summary>
///     Saves through the shared <see cref="AppDbContext" />, translating the two expected conflicts (optimistic
///     concurrency on the contract and the unique event id of a notification) into <c>false</c>.
/// </summary>
public class SubscriptionUnitOfWork(AppDbContext context) : ISubscriptionUnitOfWork
{
    public async Task<bool> TryCompleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            context.ChangeTracker.Clear();
            return false;
        }
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true;
}
