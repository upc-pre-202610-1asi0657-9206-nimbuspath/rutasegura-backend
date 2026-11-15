extern alias gatewayApi;

using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Tests.Host;

public sealed class GatewayAndSignalRTests
{
    [Fact]
    public async Task GatewayProxiesRestNegotiationAndWebSocketsWithoutExposingInternalRoutes()
    {
        await using var notification = new NotificationApiFactory();
        notification.UseKestrel(0);
        using var upstream = notification.CreateClient();
        var notice = notification.SeedNotice();
        await using var gateway = new GatewayFactory(upstream.BaseAddress!);
        gateway.UseKestrel(0);
        using var client = gateway.CreateClient();
        (await client.GetAsync("/api/v1/notifications", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/internal/v1/notifications/student-links", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        client.DefaultRequestHeaders.Authorization = new("Bearer", notification.Token());
        client.DefaultRequestHeaders.Add("Origin", "https://foreign.test");
        (await client.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Remove("Origin");
        (await client.GetAsync("/api/v1/notifications", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var recipient = await Connect(client.BaseAddress!, notification.Token());
        using var foreignTenant = await Connect(client.BaseAddress!, notification.Token(organization: Guid.NewGuid()));
        using var foreignUser = await Connect(client.BaseAddress!, notification.Token(user: Guid.NewGuid()));
        await notification.Services.GetRequiredService<IRealtimeNotificationSender>().SendAsync(new NotificationResult(notice.Id,
            notice.OrganizationId, notice.RecipientUserId, null, notice.StudentId, "Boarding", "Normal", "NO FILTRAR TITULO",
            "NO FILTRAR CONTENIDO", notice.OccurredAt, notice.CreatedAt, null), TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var received = await Receive(recipient, timeout.Token);
        received.GetProperty("target").GetString().ShouldBe("NotificationAvailable");
        var hint = received.GetProperty("arguments")[0];
        hint.GetProperty("notificationId").GetGuid().ShouldBe(notice.Id);
        hint.EnumerateObject().Count().ShouldBe(2);
        hint.ToString().ShouldNotContain("NO FILTRAR");
        await AssertNoNotice(foreignTenant);
        await AssertNoNotice(foreignUser);
    }

    private static async Task<ClientWebSocket> Connect(Uri root, string token)
    {
        var socket = new ClientWebSocket();
        try
        {
            var address = new UriBuilder(root) { Scheme = "ws", Path = "/hubs/notifications", Query = "access_token=" + Uri.EscapeDataString(token) };
            await socket.ConnectAsync(address.Uri, TestContext.Current.CancellationToken);
            await Send(socket, "{\"protocol\":\"json\",\"version\":1}\u001e");
            (await Receive(socket, TestContext.Current.CancellationToken)).EnumerateObject().ShouldBeEmpty();
            // Una completion, incluso de metodo inexistente, acredita que OnConnectedAsync termino y ya se asigno el grupo.
            await Send(socket, "{\"type\":1,\"invocationId\":\"barrier\",\"target\":\"TestBarrierMissingMethod\",\"arguments\":[]}\u001e");
            (await Receive(socket, TestContext.Current.CancellationToken)).GetProperty("type").GetInt32().ShouldBe(3);
            return socket;
        }
        catch { socket.Dispose(); throw; }
    }

    private static Task Send(ClientWebSocket socket, string text) => socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, TestContext.Current.CancellationToken);

    private static async Task<JsonElement> Receive(ClientWebSocket socket, CancellationToken ct)
    {
        var bytes = new byte[8192];
        var length = 0;
        while (true)
        {
            var part = await socket.ReceiveAsync(bytes.AsMemory(length), ct);
            if (part.MessageType == WebSocketMessageType.Close) throw new IOException("SignalR cerro la conexion de prueba.");
            length += part.Count;
            var separator = Array.IndexOf(bytes, (byte)0x1e, 0, length);
            if (separator >= 0) return JsonDocument.Parse(bytes.AsMemory(0, separator)).RootElement.Clone();
            if (length == bytes.Length) throw new IOException("Respuesta de SignalR demasiado grande.");
        }
    }

    private static async Task AssertNoNotice(ClientWebSocket socket)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(300));
        await Should.ThrowAsync<OperationCanceledException>(() => Receive(socket, timeout.Token));
    }

    private sealed class GatewayFactory(Uri upstream) : WebApplicationFactory<gatewayApi::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(Path.Combine(NotificationApiFactory.Root(), "src/ApiGateway"));
        }
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NotificationProxy:Enabled"] = "true", ["Jwt:Secret"] = NotificationApiFactory.Secret,
                ["ReverseProxy:Clusters:notification:Destinations:primary:Address"] = upstream.ToString()
            }));
            return base.CreateHost(builder);
        }
    }
}
