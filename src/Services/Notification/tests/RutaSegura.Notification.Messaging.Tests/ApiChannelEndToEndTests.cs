extern alias notificationApi;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FirebaseAdmin.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Infrastructure.Channels.Fcm;

namespace RutaSegura.Notification.Messaging.Tests;

public sealed class ApiChannelEndToEndTests : BrokerTestContext
{
    [BrokerFact]
    public async Task ReadinessChecksMigratedTablesAndDetectsMissingSchema_WithoutAffectingLiveness()
    {
        await using var factory = new ApiFactory(NotificationConnection, EncryptionKey, Directory, new CapturingFcmTransport(), channels: false);
        using var client = factory.CreateClient();
        (await client.GetAsync("/health/ready", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // Base aleatoria y exclusiva de este fixture; no se alteran tablas ni datos del entorno del usuario.
        await using (var db = OpenNotification())
            await db.Database.ExecuteSqlRawAsync("DROP TABLE notification_deliveries", Ct);
        (await client.GetAsync("/health/ready", Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await client.GetAsync("/health/live", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [BrokerFact]
    public async Task RabbitEventBecomesPersistentInboxRealtimeHintAndFcmDelivery_WithAuthenticatedReadAndRevocation()
    {
        var transport = new CapturingFcmTransport();
        await using var factory = new ApiFactory(NotificationConnection, EncryptionKey, Directory, transport);
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        // Sembrar tokens con el mismo modelo/protector del host. Los contextos manuales del fixture
        // usan otro proveedor de servicios y EF puede cachear su conversor entre bases de prueba.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var seed = scope.ServiceProvider.GetRequiredService<RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration.NotificationDbContext>();
            seed.PushSubscriptions.Add(PushSubscription.Register(MessageSamples.Organization, MessageSamples.Guardian, Guid.NewGuid(),
                "opaque-integration-token", PushPlatform.Web, DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync(Ct);
        }
        var token = AccessToken();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new UriBuilder(client.BaseAddress!) { Scheme = "ws", Path = "/hubs/notifications", Query = "access_token=" + token }.Uri, Ct);
        await Send(socket, "{\"protocol\":\"json\",\"version\":1}\u001e");
        await Receive(socket);
        await Send(socket, "{\"type\":1,\"invocationId\":\"barrier\",\"target\":\"TestBarrierMissingMethod\",\"arguments\":[]}\u001e");
        (await Receive(socket)).GetProperty("type").GetInt32().ShouldBe(3);
        var original = MessageSamples.Message();
        await Publish(original);
        var realtime = await Receive(socket);
        var id = realtime.GetProperty("arguments")[0].GetProperty("notificationId").GetGuid();
        var push = await transport.Sent.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        push.Data["notificationId"].ShouldBe(id.ToString("D"));
        push.Notification.Title.ShouldBe("RutaSegura");
        var inbox = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications", Ct);
        inbox.GetArrayLength().ShouldBe(1);
        inbox[0].GetProperty("id").GetGuid().ShouldBe(id);
        await Eventually(async () => { await using var db = OpenNotification(); return await db.Deliveries.CountAsync(d => d.Status == DeliveryStatus.ProviderAccepted, Ct) == 2; });
        await Publish(original);
        (await client.PatchAsync($"/api/v1/notifications/{id}/read", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using (var verify = OpenNotification())
        {
            (await verify.Notifications.SingleAsync(Ct)).ReadAt.ShouldNotBeNull();
            (await verify.DispatchJobs.CountAsync(Ct)).ShouldBe(1);
            (await verify.Deliveries.CountAsync(Ct)).ShouldBe(2);
        }
        Directory.LinkRevoked = true;
        (await client.GetAsync($"/api/v1/notifications/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        transport.Calls.ShouldBe(1);
    }

    private static Task Send(ClientWebSocket socket, string text) => socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, Ct);
    private static async Task<JsonElement> Receive(ClientWebSocket socket)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var bytes = new byte[8192];
        var length = 0;
        while (true)
        {
            var read = await socket.ReceiveAsync(bytes.AsMemory(length), timeout.Token);
            if (read.MessageType == WebSocketMessageType.Close) throw new IOException("WebSocket cerrado.");
            length += read.Count;
            var separator = Array.IndexOf(bytes, (byte)0x1e, 0, length);
            if (separator >= 0) return JsonDocument.Parse(bytes.AsMemory(0, separator)).RootElement.Clone();
        }
    }

    private const string Secret = "notification-e2e-test-secret-0123456789-abcdefghij";
    private static string AccessToken() => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims:
        [new Claim("sub", MessageSamples.Guardian.ToString()), new("organizationId", MessageSamples.Organization.ToString()), new("role", "PARENT")],
        expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256)));

    private sealed class CapturingFcmTransport : IFcmTransport
    {
        public TaskCompletionSource<Message> Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public Task<string> SendAsync(Message message, CancellationToken cancellationToken)
        {
            Calls++;
            Sent.TrySetResult(message);
            return Task.FromResult("projects/test/messages/captured");
        }
    }

    private sealed class ApiFactory(string connection, string encryptionKey, TestDirectory directory, IFcmTransport transport, bool channels = true)
        : WebApplicationFactory<notificationApi::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "RutaSegura.slnx"))) root = root.Parent;
            builder.UseContentRoot(Path.Combine(root!.FullName, "src/Services/Notification/src/RutaSegura.Notification.Api"));
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<INotificationRecipientDirectory>(); services.AddSingleton<INotificationRecipientDirectory>(directory);
                services.RemoveAll<ITripNotificationContext>(); services.AddSingleton<ITripNotificationContext>(directory);
                services.RemoveAll<IFcmTransport>(); services.AddSingleton(transport);
            });
        }
        protected override IHost CreateHost(IHostBuilder builder)
        {
            var settings = new Dictionary<string, string?>
            {
                ["NotificationApi:Enabled"] = "true", ["NotificationChannels:Enabled"] = channels.ToString(), ["NotificationRabbitMq:Enabled"] = "false",
                ["Iam:AccountValidation:Enabled"] = "false", // Directorios e identidad remota se aislan en este E2E de canales.
                ["NotificationChannels:PollingInterval"] = "00:00:00.100", ["NotificationFcm:Enabled"] = "true", ["NotificationFcm:ProjectId"] = "test",
                ["ConnectionStrings:NotificationDatabase"] = connection, ["NotificationPersistence:PushTokenEncryptionKey"] = encryptionKey,
                ["Jwt:Secret"] = Secret
            };
            foreach (var dependency in new[] { "Administration", "TripTracking" })
            {
                settings[$"NotificationDependencies:{dependency}:BaseUrl"] = "https://service.internal/";
                settings[$"NotificationDependencies:{dependency}:ServiceToken"] = new string('s', 32);
            }
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(settings));
            return base.CreateHost(builder);
        }
    }
}
