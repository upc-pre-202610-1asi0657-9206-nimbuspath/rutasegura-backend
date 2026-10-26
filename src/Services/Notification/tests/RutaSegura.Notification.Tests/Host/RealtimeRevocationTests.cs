extern alias notificationApi;

using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Verifier = notificationApi::RutaSegura.Notification.Api.Security.INotificationIamAccountVerifier;
using AccountState = notificationApi::RutaSegura.Notification.Api.Security.NotificationIamAccountState;

namespace RutaSegura.Notification.Tests.Host;

public sealed class RealtimeRevocationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEstablishedWebSocketIsClosedWhenIamRevokesTheAccountOrCannotValidateIt(bool unavailable)
    {
        var probe=new AccountProbe();
        await using var factory=new NotificationApiFactory(new()
        {
            ["Iam:AccountValidation:Enabled"]="true", ["Iam:BaseUrl"]="https://iam.internal",
            ["Jwt:Issuer"]="rutasegura-iam", ["Jwt:Audience"]="rutasegura-api", ["NotificationRealtime:ValidationInterval"]="00:00:00.100"
        }, services=> { services.RemoveAll<Verifier>(); services.AddSingleton<Verifier>(probe); });
        factory.UseKestrel(0);
        using var client=factory.CreateClient();
        var token=new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("rutasegura-iam","rutasegura-api",
            [new("sub",factory.Guardian.ToString()), new("organizationId",factory.Organization.ToString()), new(ClaimTypes.Role,"PARENT")],
            expires:DateTime.UtcNow.AddMinutes(5), signingCredentials:new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(NotificationApiFactory.Secret)),SecurityAlgorithms.HmacSha256)));
        using var socket=new ClientWebSocket();
        await socket.ConnectAsync(new UriBuilder(client.BaseAddress!) { Scheme="ws",Path="/hubs/notifications",Query="access_token="+token }.Uri,Ct);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),WebSocketMessageType.Text,true,Ct);
        var buffer=new byte[4096];
        await socket.ReceiveAsync(buffer.AsMemory(),Ct);
        probe.State=unavailable ? AccountState.Unavailable : AccountState.Rejected;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(Ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var closed=false;
        try
        {
            while(!closed)
            {
                var read=await socket.ReceiveAsync(buffer.AsMemory(),timeout.Token);
                closed=read.MessageType==WebSocketMessageType.Close || Encoding.UTF8.GetString(buffer,0,read.Count).Contains("\"type\":7",StringComparison.Ordinal);
            }
        }
        catch(WebSocketException) { closed=true; }
        closed.ShouldBeTrue(); probe.Calls.ShouldBeGreaterThanOrEqualTo(2);
    }

    private sealed class AccountProbe : Verifier
    {
        private int _state, _calls;
        public AccountState State { get=> (AccountState)Volatile.Read(ref _state); set=>Volatile.Write(ref _state,(int)value); }
        public int Calls => Volatile.Read(ref _calls);
        public Task<AccountState> VerifyAsync(string token,Guid userId,Guid organizationId,string role,CancellationToken ct)
        { Interlocked.Increment(ref _calls); ct.ThrowIfCancellationRequested(); return Task.FromResult(State); }
    }
}
