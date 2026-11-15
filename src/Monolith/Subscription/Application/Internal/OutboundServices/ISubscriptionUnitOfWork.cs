namespace Monolith.Subscription.Application.Internal.OutboundServices;

/// <summary>
///     Saves the changes of a Subscription use case. Unlike the shared unit of work it reports a conflict instead of
///     throwing a persistence exception, so the application layer stays free of EF Core: another request changed the
///     same contract first (optimistic concurrency) or already recorded the same payment notification (unique key).
/// </summary>
public interface ISubscriptionUnitOfWork
{
    /// <returns>False when the save lost against a concurrent change; nothing was saved and the caller may retry.</returns>
    Task<bool> TryCompleteAsync(CancellationToken cancellationToken);
}
