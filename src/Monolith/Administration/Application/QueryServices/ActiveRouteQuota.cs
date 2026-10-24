namespace Monolith.Administration.Application.QueryServices;

/// <summary>Read model: active routes of the organization against the limit of the contracted plan.</summary>
public record ActiveRouteQuota(string PlanCode, int ActiveRoutes, int MaxActiveRoutes)
{
    public int RemainingActiveRoutes => Math.Max(0, MaxActiveRoutes - ActiveRoutes);

    public bool LimitReached => ActiveRoutes >= MaxActiveRoutes;
}
