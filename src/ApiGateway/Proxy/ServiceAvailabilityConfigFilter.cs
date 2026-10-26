using Yarp.ReverseProxy.Configuration;

namespace RutaSegura.ApiGateway.Proxy;

/// <summary>
/// Notification todavía es opcional (<c>NotificationProxy:Enabled</c>). Sus rutas siguen en la configuración para no
/// romper a quien la sobrescribe, pero si está deshabilitado se marcan como tales (el Gateway responde 503
/// <c>SERVICE_DISABLED</c> sin intentar conectarse) y se les quita la política de autorización, que no existe.
/// </summary>
internal sealed class ServiceAvailabilityConfigFilter(IConfiguration configuration) : IProxyConfigFilter
{
    private bool NotificationEnabled => configuration.GetValue<bool>("NotificationProxy:Enabled");

    public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel) => ValueTask.FromResult(cluster);

    public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
    {
        if (NotificationEnabled || !string.Equals(route.ClusterId, GatewayNames.NotificationCluster, StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult(route);

        var metadata = new Dictionary<string, string>(route.Metadata ?? new Dictionary<string, string>())
        {
            [GatewayNames.DisabledMetadataKey] = "true"
        };
        return ValueTask.FromResult(route with { AuthorizationPolicy = null, Metadata = metadata });
    }
}
