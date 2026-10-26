extern alias gateway;

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Yarp.ReverseProxy.Forwarder;
using GatewayProgram = gateway::Program;

namespace RutaSegura.ApiGateway.Tests.Support;

/// <summary>Direcciones de los clusters en las pruebas: cada "host" se resuelve a un servidor en memoria.</summary>
public static class Upstreams
{
    public const string Monolith = "http://monolith.test/";
    public const string TripTracking = "http://trip-tracking.test/";
    public const string Notification = "http://notification.test/";
}

/// <summary>
/// El API Gateway real (Program.cs, appsettings.json con sus rutas) con los clusters apuntando a servidores en
/// memoria: YARP y el chequeo de readiness salen por <see cref="Network"/>, que entrega cada request al servidor
/// del host de destino (o falla como una conexión rechazada si está "caído").
/// </summary>
public sealed class GatewayHost(IDictionary<string, string?>? overrides = null) : WebApplicationFactory<GatewayProgram>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FakeNetwork Network { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var settings = new Dictionary<string, string?>
        {
            ["ReverseProxy:Clusters:monolith:Destinations:primary:Address"] = Upstreams.Monolith,
            ["ReverseProxy:Clusters:trip-tracking:Destinations:primary:Address"] = Upstreams.TripTracking,
            ["ReverseProxy:Clusters:notification:Destinations:primary:Address"] = Upstreams.Notification,
            ["NotificationProxy:Enabled"] = "false"
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>()) settings[key] = value;
        foreach (var (key, value) in settings) builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IForwarderHttpClientFactory>();
            services.AddSingleton<IForwarderHttpClientFactory>(new NetworkForwarderFactory(Network));
            services.AddHttpClient("gateway-readiness").ConfigurePrimaryHttpMessageHandler(() => Network.Handler());
        });
    }

    public HttpClient Client(string? token = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class NetworkForwarderFactory(FakeNetwork network) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(network.Handler(), disposeHandler: false);
    }
}

/// <summary>"Red" de las pruebas: host → servidor en memoria. Un host sin servidor (o apagado) rechaza la conexión.</summary>
public sealed class FakeNetwork
{
    private readonly ConcurrentDictionary<string, HttpMessageHandler> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _down = new(StringComparer.OrdinalIgnoreCase);

    public void Attach(string baseAddress, HttpMessageHandler server) => _servers[new Uri(baseAddress).Host] = server;

    public void TurnOff(string baseAddress) => _down[new Uri(baseAddress).Host] = true;

    public HttpMessageHandler Handler() => new Router(this);

    private sealed class Router(FakeNetwork network) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri!.Host;
            if (network._down.ContainsKey(host) || !network._servers.TryGetValue(host, out var server))
                throw new HttpRequestException($"Connection refused ({host})");
            return new HttpMessageInvoker(server, disposeHandler: false).SendAsync(request, cancellationToken);
        }
    }
}

/// <summary>Servicio "eco": responde qué recibió (servicio, método, ruta, query y cabeceras relevantes).</summary>
public sealed class EchoService : IAsyncDisposable
{
    private readonly WebApplication _app;

    private EchoService(WebApplication app) => _app = app;

    public HttpMessageHandler Handler => _app.GetTestServer().CreateHandler();

    public static async Task<EchoService> StartAsync(string name)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.Map("/{**path}", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            return Results.Json(new EchoResponse(
                name,
                context.Request.Method,
                context.Request.Path.Value ?? "",
                context.Request.QueryString.Value ?? "",
                context.Request.Headers.Authorization.ToString(),
                context.Request.Headers["X-Correlation-Id"].ToString(),
                context.Request.Headers["X-Forwarded-For"].ToString(),
                context.Request.Headers["X-Forwarded-Proto"].ToString(),
                body));
        });
        await app.StartAsync();
        return new EchoService(app);
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

public sealed record EchoResponse(
    string Service,
    string Method,
    string Path,
    string Query,
    string Authorization,
    string CorrelationId,
    string ForwardedFor,
    string ForwardedProto,
    string Body);
