namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     Limits granted by the plan the operation has contracted. Owned by the Subscription context (not built
///     yet); Route Planning only reads them through an application port and never stores them.
/// </summary>
public record PlanLimits
{
    public PlanLimits(string planCode, int maxActiveRoutes)
    {
        if (string.IsNullOrWhiteSpace(planCode))
            throw new ArgumentException("Plan code is required.", nameof(planCode));
        if (maxActiveRoutes < 0)
            throw new ArgumentException("The maximum number of active routes cannot be negative.",
                nameof(maxActiveRoutes));

        PlanCode = planCode.Trim().ToUpperInvariant();
        MaxActiveRoutes = maxActiveRoutes;
    }

    public string PlanCode { get; init; }

    public int MaxActiveRoutes { get; init; }

    public bool AllowsAnotherActiveRoute(int currentActiveRoutes) => currentActiveRoutes < MaxActiveRoutes;

    public int RemainingActiveRoutes(int currentActiveRoutes) => Math.Max(0, MaxActiveRoutes - currentActiveRoutes);
}
