namespace Monolith.Subscription.Interfaces.ACL;

/// <summary>
///     Published language of Subscription: the only thing other bounded contexts use from it (Customer/Supplier with
///     Route Planning, ADR-05.4). They ask for the limits in force and never read the subscription tables.
/// </summary>
public interface ISubscriptionContextFacade
{
    /// <summary>
    ///     Limits that apply now to the organization: those of its plan while it is paid, otherwise those of the
    ///     default plan (organizations that never contracted a plan included).
    /// </summary>
    Task<SubscriptionLimits> GetCurrentLimitsAsync(Guid organizationId, CancellationToken cancellationToken);
}

public record SubscriptionLimits(string PlanCode, int MaxActiveRoutes, int MaxVehicles);
