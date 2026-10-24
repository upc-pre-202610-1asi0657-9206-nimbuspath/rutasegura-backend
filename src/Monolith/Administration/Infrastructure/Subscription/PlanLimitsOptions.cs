namespace Monolith.Administration.Infrastructure.Subscription;

/// <summary>
///     Plans per organization while the Subscription context does not exist (section <c>Administration:Plans</c>).
///     Organizations without an entry get <see cref="Default" />.
/// </summary>
public sealed class PlanLimitsOptions
{
    public const string Section = "Administration:Plans";

    public PlanSettings Default { get; set; } = new();

    /// <summary>Key: organization id (GUID).</summary>
    public Dictionary<string, PlanSettings> Organizations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PlanSettings
{
    public string PlanCode { get; set; } = "BASIC";
    public int MaxActiveRoutes { get; set; } = 3;
}
