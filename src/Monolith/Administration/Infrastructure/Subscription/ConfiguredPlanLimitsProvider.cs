using Microsoft.Extensions.Options;
using Monolith.Administration.Application.Internal.OutboundServices;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Administration.Infrastructure.Subscription;

/// <summary>
///     Temporary adapter of <see cref="IPlanLimitsProvider" />: reads the plan from configuration. When the
///     Subscription context exists, only this class is replaced by one that calls its facade.
/// </summary>
public class ConfiguredPlanLimitsProvider(IOptionsMonitor<PlanLimitsOptions> options) : IPlanLimitsProvider
{
    public Task<PlanLimits> GetCurrentLimitsAsync(OrganizationId organizationId, CancellationToken cancellationToken)
    {
        var plans = options.CurrentValue;
        var plan = plans.Organizations.TryGetValue(organizationId.Identifier.ToString(), out var specific)
            ? specific
            : plans.Default;
        return Task.FromResult(new PlanLimits(plan.PlanCode, plan.MaxActiveRoutes));
    }
}
