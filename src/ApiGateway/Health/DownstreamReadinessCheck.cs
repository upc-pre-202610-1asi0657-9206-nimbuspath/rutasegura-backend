using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RutaSegura.ApiGateway.Proxy;
using Yarp.ReverseProxy;

namespace RutaSegura.ApiGateway.Health;

/// <summary>
/// <c>/health/ready</c>: el Gateway solo está listo si cada servicio de <c>Gateway:Readiness</c> responde su endpoint de
/// salud (monolito <c>/health</c>, Trip &amp; Tracking <c>/health/live</c>). Los clusters deshabilitados no cuentan.
/// </summary>
internal sealed class DownstreamReadinessCheck(
    IProxyStateLookup proxyState,
    IHttpClientFactory httpClientFactory,
    IOptions<GatewayOptions> options,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var data = new Dictionary<string, object>();
        var failures = new List<string>();
        var client = httpClientFactory.CreateClient(GatewayNames.ReadinessHttpClient);

        foreach (var (clusterId, healthPath) in settings.Readiness)
        {
            if (string.Equals(clusterId, GatewayNames.NotificationCluster, StringComparison.OrdinalIgnoreCase) &&
                !configuration.GetValue<bool>("NotificationProxy:Enabled"))
                continue;

            if (!proxyState.TryGetCluster(clusterId, out var cluster) || cluster.Destinations.IsEmpty)
            {
                failures.Add(clusterId);
                data[clusterId] = "sin destinos configurados";
                continue;
            }

            var address = cluster.Destinations.Values.First().Model.Config.Address;
            var target = new Uri(new Uri(address.EndsWith('/') ? address : address + "/"), healthPath.TrimStart('/'));
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(settings.ReadinessTimeout);
                using var response = await client.GetAsync(target, timeout.Token);
                data[clusterId] = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode) failures.Add(clusterId);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                data[clusterId] = "sin respuesta";
                failures.Add(clusterId);
            }
        }

        return failures.Count == 0
            ? HealthCheckResult.Healthy("Servicios disponibles", data)
            : HealthCheckResult.Unhealthy($"Sin respuesta: {string.Join(", ", failures)}", data: data);
    }
}
